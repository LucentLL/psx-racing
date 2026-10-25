using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DRAWN PROFILE'S CENSUS (2026-10-25; CityElevation.VSmooth.cs). The
    /// owner: "every bump in the road is angular ... no harsh angles to adjust
    /// height/elevation, ever."
    ///
    /// Reads the ribbon the tile builder EMITS (CityMeshes.RecordTap: each
    /// quad's two sections, their heights off the mesh), not the stations,
    /// and measures it twice in one job - the solve's straight stations
    /// (CityElevation.ApplyVSmooth off) and the drawn profile (on) - over the
    /// same tiles:
    ///   - every grade break between consecutive drawn sections along each
    ///     ribbon (and through each two-arm node), by road class: how many
    ///     past 0.5 / 1 / 2 %, and how many whose local radius (mean spacing
    ///     over the break) is under what the road needs - AASHTO's comfort
    ///     radius at the design speed for a sag, the crest limiter's radius
    ///     for a crest;
    ///   - along every race route, the vertical acceleration a car feels
    ///     (v^2 times the change of grade over a 2.5 m wheelbase), at
    ///     150 mph and at the posted speed: max and p95, sags (pressed into
    ///     the road: bottoming) and crests (lifted) apart;
    ///   - what the knots cost: drawn sections before / after.
    /// The AFTER residuals past 0.5% on freeways, ramps and arterials are
    /// listed with the reason their curve is short (CityElevation.VsCapped).
    ///
    /// PSX_VS_TILES=routes: only the tiles within 300 m of a race route
    /// (default: every road tile). PSX_VS_LAUNCH=1: the launch audit after,
    /// once each way (city_launch_before.txt / _after.txt).
    /// Writes vsmooth_census.txt and vsmooth_residuals.csv in the project root.
    /// </summary>
    public static class CityVSmoothCensus
    {
        const float MinH = 0.3f;
        const float WheelbaseM = 2.5f;
        const float Mph150 = 150f * 0.44704f;

        struct Seg { public float sA, yA, sB, yB; }
        class Stats
        {
            public string label;
            public long sections, spans;
            public readonly Dictionary<string, int[]> byGroup = new Dictionary<string, int[]>();
            public readonly List<(string group, float A, float R, float need, int edge, float s, float x, float z)> worst = new List<(string, float, float, float, int, float, float, float)>();
            public readonly List<string> routeLines = new List<string>();
            public double tileSeconds;
            // the retaining walls the tiles built (CityMeshes.InCut), by why and by edge
            public float cutWall;
            public readonly float[] cutWhy = new float[6];
            public readonly Dictionary<int, (float m, byte why, Vector2 at)> cutByEdge = new Dictionary<int, (float, byte, Vector2)>();
        }
        // byGroup counters: 0 breaks >0.1%, 1 >0.5%, 2 >1%, 3 >2% (on the profile), 4 tight sag curves, 5 tight crest curves,
        // 6 node joints >0.5%, 7 breaks >0.5% where the mesh stands off the profile (a fan, a clip, a structure end), 8 curves >0.5%
        static readonly string[] Groups = { "freeway", "ramp", "arterial", "collector", "local" };

        static string GroupOf(CityMap.Edge e) =>
            e.link ? "ramp" : e.cls >= 4 ? "freeway" : e.cls >= 2 ? "arterial" : e.cls == 1 ? "collector" : "local";

        [MenuItem("PSX Racing/City/Drawn profile census")]
        public static void Run()
        {
            var log = new StringBuilder();
            void Line(string s) { log.AppendLine(s); Debug.Log("[VSmooth] " + s); }
            string root = Directory.GetParent(Application.dataPath).FullName;
            int status = 1;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                CityElevation.KeepTerrainProfiles = true;
                var map = CityMap.Get();
                CityElevation.KeepTerrainProfiles = false;
                if (map == null) throw new Exception("no city data");
                Line($"DRAWN PROFILE CENSUS - {map.edges.Length} edges; solve {CityMap.LastSolveMs:0} ms ({CityElevation.LastSolvePhases})");
                Line(CityElevation.VSmoothReport);
                bool routesOnly = Environment.GetEnvironmentVariable("PSX_VS_TILES") == "routes";
                var tiles = Environment.GetEnvironmentVariable("PSX_VS_TILES") == "none" ? new List<(int, int)>() : Tiles(map, routesOnly);
                Line($"tiles: {tiles.Count} ({(routesOnly ? "within 300 m of a race route" : "every road tile")})");

                string shots = Environment.GetEnvironmentVariable("PSX_VS_SHOTS");
                var shotsB = new List<Texture2D>(); var shotsA = new List<Texture2D>();
                CityElevation.ApplyVSmooth(map, false);
                string twinB = TwinDy(map);
                var before = tiles.Count > 0 ? Census(map, tiles, "BEFORE (stations, straight)") : new Stats();
                if (!string.IsNullOrEmpty(shots)) Shots(map, shots, shotsB);
                CityElevation.ApplyVSmooth(map, true);
                Line(CityElevation.VSmoothReport);
                Line("TWIN decks (union pairs, side by side every 2 m): BEFORE " + twinB + " -> AFTER " + TwinDy(map));
                var after = tiles.Count > 0 ? Census(map, tiles, "AFTER (drawn profile)") : new Stats();
                if (!string.IsNullOrEmpty(shots)) { Shots(map, shots, shotsA); SaveShots(Path.Combine(root, "Screenshots", "vsmooth"), shotsB, shotsA, Line); }

                Line("");
                Line("GRADE BREAKS between consecutive drawn sections (spacing >= 0.3 m) where the mesh is on the height profile, BEFORE -> AFTER;");
                Line("CURVES = runs of same-sign breaks (each >= 0.05%) totalling >= 0.5%, TIGHT = mean radius (length / total) under 0.9 x the need");
                Line("(sag: AASHTO comfort at the design speed; crest: the crest limiter's radius); MESH-OFF = breaks >0.5% where the drawn centre stands >2 cm off YAt (fans, clips, structure ends - not the profile's)");
                Line("  group      >0.1%            >0.5%            >1%              >2%              node joints>0.5%  curves           TIGHT sag        TIGHT crest      MESH-OFF>0.5%");
                foreach (var g in Groups)
                {
                    before.byGroup.TryGetValue(g, out var b); after.byGroup.TryGetValue(g, out var a);
                    b ??= new int[9]; a ??= new int[9];
                    Line(string.Format(CultureInfo.InvariantCulture, "  {0,-10} {1,6} -> {2,-7} {3,6} -> {4,-7} {5,6} -> {6,-7} {7,6} -> {8,-7} {9,6} -> {10,-8} {11,6} -> {12,-7} {13,6} -> {14,-7} {15,6} -> {16,-7} {17,6} -> {18,-7}",
                        g, b[0], a[0], b[1], a[1], b[2], a[2], b[3], a[3], b[6], a[6], b[8], a[8], b[4], a[4], b[5], a[5], b[7], a[7]));
                }
                Line($"  drawn sections {before.sections} -> {after.sections} ({(after.sections - before.sections) * 100.0 / Math.Max(1, before.sections):+0.0;-0.0}%), quads {before.spans} -> {after.spans}; tile builds {before.tileSeconds:0} s -> {after.tileSeconds:0} s");
                {
                    // RETAINING WALLS (CityMeshes.InCut): the walled cut length the
                    // tiles built, by why; the walls REAL HEIGHTS adds are the
                    // no-back-slope ones (why 5: before, a 1V:2H grass bank)
                    var cw = after.cutWhy;
                    Line(string.Format(CultureInfo.InvariantCulture, "  RETAINING WALLS (cut walls + barrier): {0:0.00} km - the back slope cannot reach the land {1:0} m, a road above in the slope {2:0} m, a building {3:0} m, held land {4:0} m, runs closed over a gap {5:0} m, NO ROOM FOR THE 1V:3H BACK SLOPE (added) {6:0} m",
                        after.cutWall / 1000f, cw[1], cw[2], cw[3], cw[4], cw[0], cw[5]));
                    var top = new List<KeyValuePair<int, (float m, byte why, Vector2 at)>>(after.cutByEdge);
                    top.RemoveAll(kv => kv.Value.why != CityMeshes.CutWhyNoBackSlope);
                    top.Sort((p, q) => q.Value.m.CompareTo(p.Value.m));
                    var sbw = new System.Text.StringBuilder("  the longest added: ");
                    for (int k = 0; k < Math.Min(8, top.Count); k++)
                        sbw.Append(string.Format(CultureInfo.InvariantCulture, "e{0} '{1}' {2:0} m at ({3:0},{4:0}); ", top[k].Key, map.edges[top[k].Key].name, top[k].Value.m, top[k].Value.at.x, top[k].Value.at.y));
                    Line(sbw.ToString());
                }
                Line("");
                Line("VERTICAL ACCELERATION along the race routes (v^2 x grade change over a 2.5 m wheelbase), in g: max |a| / p95 |a| / max sag (pressed down) / max crest (lifted)");
                for (int i = 0; i < before.routeLines.Count; i++)
                {
                    Line("  BEFORE " + before.routeLines[i]);
                    Line("  AFTER  " + (i < after.routeLines.Count ? after.routeLines[i] : "-"));
                }
                Line("");
                // the residuals, with the reason their curve is short
                var capped = new Dictionary<int, List<CityElevation.VsFillet>>();
                foreach (var f in CityElevation.VsCapped) { if (!capped.TryGetValue(f.edge, out var l)) capped[f.edge] = l = new List<CityElevation.VsFillet>(); l.Add(f); }
                var causeCount = new Dictionary<string, int>();
                var csv = new StringBuilder("group,A_pct,R_m,need_m,edge,name,cls,s,x,z,reason\n");
                after.worst.Sort((p, q) => Mathf.Abs(q.A).CompareTo(Mathf.Abs(p.A)));
                int listed = 0;
                foreach (var w in after.worst)
                {
                    string why = "no curve here (a junction's fan, a chain end or a minor arm's node)";
                    if (capped.TryGetValue(w.edge, out var fl))
                    {
                        float bd = 8f;
                        foreach (var f in fl) if (Mathf.Abs(f.s - w.s) < bd) { bd = Mathf.Abs(f.s - w.s); why = CityElevation.VsCauseName(f.cause) + string.Format(CultureInfo.InvariantCulture, " (L {0:0.0} of {1:0.0} m)", f.L, f.Lwant); }
                    }
                    string key = w.group + ": " + (why.Contains("(L") ? why.Substring(0, why.IndexOf(" (L", StringComparison.Ordinal)) : why);
                    causeCount[key] = (causeCount.TryGetValue(key, out int cc) ? cc : 0) + 1;
                    var e = map.edges[w.edge];
                    csv.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.000},{2:0},{3:0},{4},\"{5}\",{6},{7:0.0},{8:0.0},{9:0.0},\"{10}\"\n", w.group, w.A * 100f, w.R, w.need, w.edge, e.name, e.cls, w.s, w.x, w.z, why));
                    if (listed++ < 40 && (w.group == "freeway" || w.group == "ramp" || w.group == "arterial"))
                        Line(string.Format(CultureInfo.InvariantCulture, "  AFTER {0,-9} {1,6:+0.00;-0.00}% R {2,6:0} m (need {3:0}) e{4} '{5}' s {6:0.0} at ({7:0.0}, {8:0.0}): {9}", w.group, w.A * 100f, w.R, w.need, w.edge, e.name, w.s, w.x, w.z, why));
                }
                before.worst.Sort((p, q) => q.A.CompareTo(p.A));
                Line("BEFORE: the sharpest SAGS on freeways, ramps and arterials (screenshot candidates):");
                int shownB = 0;
                foreach (var w in before.worst)
                {
                    if (w.A <= 0f || !(w.group == "freeway" || w.group == "ramp" || w.group == "arterial")) continue;
                    var e = map.edges[w.edge];
                    float yb = e.StationYAt(w.s);
                    Line(string.Format(CultureInfo.InvariantCulture, "  BEFORE {0,-9} {1,6:+0.00}% e{2} '{3}' s {4:0.0} at ({5:0.0}, {6:0.0}) y {7:0.00}, drawn now {8:+0.00;-0.00} m", w.group, w.A * 100f, w.edge, e.name, w.s, w.x, w.z, yb, e.YAt(w.s) - yb));
                    if (++shownB >= 15) break;
                }
                Line("AFTER residuals past 0.5% by reason:");
                var keys = new List<string>(causeCount.Keys); keys.Sort();
                foreach (var k in keys) Line($"  {k}: {causeCount[k]}");
                var byCause = new Dictionary<string, int>();
                foreach (var f in CityElevation.VsCapped) { string k = GroupOf(map.edges[f.edge]) + " " + (f.A > 0 ? "sag" : "crest") + ": " + CityElevation.VsCauseName(f.cause); byCause[k] = (byCause.TryGetValue(k, out int c2) ? c2 : 0) + 1; }
                Line("curves short of their radius (all, by reason):");
                keys = new List<string>(byCause.Keys); keys.Sort();
                foreach (var k in keys) Line($"  {k}: {byCause[k]}");
                File.WriteAllText(Path.Combine(root, "vsmooth_residuals.csv"), csv.ToString());
                if (CityElevation.VsDump.Length > 0) File.WriteAllText(Path.Combine(root, "vsmooth_dump.txt"), CityElevation.VsDump.ToString());

                // PSX_VS_LAUNCH=1: the launch audit with the drawn profile off,
                // then on; =lanes (REAL HEIGHTS): drawn on both times, the
                // audit's old movement paths (before) then the lane carried
                // through splits and merges (after, CityLaunchAudit.LaneCarry)
                string vsl = Environment.GetEnvironmentVariable("PSX_VS_LAUNCH");
                if (vsl == "1" || vsl == "lanes")
                {
                    CityLaunchAudit.NoExit = true;
                    bool lanes = vsl == "lanes", carry0 = CityLaunchAudit.LaneCarry;
                    foreach (bool on in new[] { false, true })
                    {
                        CityElevation.ApplyVSmooth(map, lanes || on);
                        if (lanes) CityLaunchAudit.LaneCarry = on;
                        Environment.SetEnvironmentVariable("PSX_LAUNCH_LABEL", on ? "vs_after" : "vs_before");
                        CityLaunchAudit.Run();
                        string tag = on ? "after" : "before";
                        foreach (var f in new[] { "city_launch.txt", "city_launch.csv", "city_launch.json" })
                        {
                            var src = Path.Combine(root, f);
                            if (File.Exists(src)) File.Copy(src, Path.Combine(root, Path.GetFileNameWithoutExtension(f) + "_" + tag + Path.GetExtension(f)), true);
                        }
                        Line($"launch audit {tag}: city_launch_{tag}.txt");
                    }
                    CityLaunchAudit.NoExit = false;
                    CityLaunchAudit.LaneCarry = carry0;
                }
                status = 0;
            }
            catch (Exception ex) { Line("THREW " + ex); Debug.LogException(ex); }
            finally { CityMeshes.RecordTap = false; }
            Line($"done in {clock.Elapsed.TotalSeconds:0} s");
            File.WriteAllText(Path.Combine(root, "vsmooth_census.txt"), log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(status);
        }

        static List<(int tx, int tz)> Tiles(CityMap map, bool routesOnly)
        {
            var seen = new HashSet<long>(); var o = new List<(int, int)>();
            float T = CityMeshes.TileSize;
            void Cover(CityMap.Edge e, float r)
            {
                for (float s = 0f; ; s += 4f)
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    int x0 = Mathf.FloorToInt((p.x - r) / T), x1 = Mathf.FloorToInt((p.x + r) / T), z0 = Mathf.FloorToInt((p.y - r) / T), z1 = Mathf.FloorToInt((p.y + r) / T);
                    for (int tx = x0; tx <= x1; tx++) for (int tz = z0; tz <= z1; tz++) if (seen.Add(((long)tx << 32) ^ (uint)tz)) o.Add((tx, tz));
                    if (s >= e.length) break;
                }
            }
            if (routesOnly) { foreach (var rt in map.routes) foreach (int ei in rt.edges) Cover(map.edges[ei], 300f); }
            else foreach (var e in map.edges) Cover(e, e.width * 0.5f + 1f);
            o.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item1.CompareTo(b.Item1));
            return o;
        }

        static Stats Census(CityMap map, List<(int tx, int tz)> tiles, string label)
        {
            var st = new Stats { label = label };
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var segs = new Dictionary<int, List<Seg>>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CityMeshes.RecordTap = true;
            foreach (var (tx, tz) in tiles)
            {
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                try
                {
                    st.cutWall += tm.cutWallM;
                    for (int w = 0; w < st.cutWhy.Length && w < tm.cutWallByWhy.Length; w++) st.cutWhy[w] += tm.cutWallByWhy[w];
                    foreach (var kv in tm.cutWallByEdge)
                        st.cutByEdge[kv.Key] = st.cutByEdge.TryGetValue(kv.Key, out var cw) ? (cw.m + kv.Value.m, cw.why, cw.at) : kv.Value;
                    var tap = tm.tap;
                    if (tap == null || tm.roads == null || tap.slotBase == null) continue;
                    var verts = tm.roads.vertices;
                    float oy = tm.origin.y;
                    foreach (var sp in tap.spans)
                    {
                        int b = -1;
                        for (int i = 0; i < tm.roadSlots.Length; i++) if ((int)tm.roadSlots[i] == sp.slot) { b = tap.slotBase[i]; break; }
                        int nq = Math.Max(1, sp.strips);
                        if (b < 0) continue;
                        int i0 = b + sp.bucketV, il = i0 + 4 * (nq - 1);
                        if (il + 3 >= verts.Length) continue;
                        if (((sp.flagsA | sp.flagsB) & CityMeshes.RoadTap.FCollapsed) != 0) continue;
                        float YA = SideY(sp.flagsA, verts[i0].y, verts[il + 3].y) + oy;
                        float YB = SideY(sp.flagsB, verts[i0 + 1].y, verts[il + 2].y) + oy;
                        if (!segs.TryGetValue(sp.edge, out var l)) segs[sp.edge] = l = new List<Seg>();
                        l.Add(new Seg { sA = Mathf.Min(sp.sA, sp.sB), yA = sp.sA <= sp.sB ? YA : YB, sB = Mathf.Max(sp.sA, sp.sB), yB = sp.sA <= sp.sB ? YB : YA });
                        st.spans++;
                    }
                }
                finally
                {
                    foreach (var f in typeof(CityMeshes.TileMeshes).GetFields())
                        if (f.FieldType == typeof(Mesh) && f.GetValue(tm) is Mesh m && m != null) UnityEngine.Object.DestroyImmediate(m);
                }
            }
            CityMeshes.RecordTap = false;
            st.tileSeconds = clock.Elapsed.TotalSeconds;

            // per edge: contiguous runs of the drawn ribbon, as polylines
            var runs = new Dictionary<int, List<List<Vector2>>>();
            foreach (var kv in segs)
            {
                var l = kv.Value;
                l.Sort((p, q) => p.sA.CompareTo(q.sA));
                var rl = new List<List<Vector2>>();
                List<Vector2> cur = null; float lastB = float.NegativeInfinity;
                foreach (var g in l)
                {
                    if (g.sB - g.sA < 1e-3f) continue;
                    if (cur == null || Mathf.Abs(g.sA - lastB) > 0.02f) { cur = new List<Vector2> { new Vector2(g.sA, g.yA) }; rl.Add(cur); }
                    cur.Add(new Vector2(g.sB, g.yB));
                    lastB = g.sB;
                }
                // drop points within MinH of the one before (a sliver section)
                foreach (var r in rl)
                {
                    int w = 1;
                    for (int i = 1; i < r.Count; i++) if (r[i].x - r[w - 1].x >= MinH || i == r.Count - 1) r[w++] = r[i];
                    r.RemoveRange(w, r.Count - w);
                    if (r.Count >= 2 && r[r.Count - 1].x - r[r.Count - 2].x < MinH && r.Count >= 3) r.RemoveAt(r.Count - 2);
                    st.sections += r.Count;
                }
                runs[kv.Key] = rl;
            }

            bool[] routeEdge = new bool[map.edges.Length];
            foreach (var rt in map.routes) foreach (int ei in rt.edges) routeEdge[ei] = true;
            int[] Ctr(CityMap.Edge e)
            {
                string g = GroupOf(e);
                if (!st.byGroup.TryGetValue(g, out var c)) st.byGroup[g] = c = new int[9];
                return c;
            }
            void Count(CityMap.Edge e, float A, float h0, float h1, float s, bool joint, bool off)
            {
                var c = Ctr(e);
                float aa = Mathf.Abs(A);
                if (aa < 0.001f) return;
                if (off) { if (aa > 0.005f) c[7]++; return; }
                c[0]++;
                if (aa > 0.005f) { c[1]++; if (joint) c[6]++; }
                if (aa > 0.01f) c[2]++;
                if (aa > 0.02f) c[3]++;
                if (aa > 0.005f)
                {
                    float R = 0.5f * (h0 + h1) / aa;
                    float need = A > 0f ? CityElevation.SagComfortR(CityElevation.VerticalMph(e)) : CityElevation.CrestRadius(e, routeEdge[e.index]);
                    var p = e.PointAt(s);
                    st.worst.Add((GroupOf(e), A, R, need, e.index, s, p.x, p.y));
                }
            }
            foreach (var kv in runs)
            {
                var e = map.edges[kv.Key];
                float needSag = CityElevation.SagComfortR(CityElevation.VerticalMph(e)), needCrest = CityElevation.CrestRadius(e, routeEdge[e.index]);
                foreach (var r in kv.Value)
                {
                    var dev = new bool[r.Count];
                    for (int i = 0; i < r.Count; i++) dev[i] = Mathf.Abs(r[i].y - e.YAt(r[i].x)) > 0.02f;
                    // the curves: runs of same-sign breaks
                    float sumA = 0f, cStart = 0f, cEnd = 0f; int sign = 0;
                    void Close()
                    {
                        if (sign == 0) return;
                        if (Mathf.Abs(sumA) >= 0.005f)
                        {
                            var c = Ctr(e);
                            c[8]++;
                            float R = (cEnd - cStart) / Mathf.Abs(sumA);
                            if (R < 0.9f * (sign > 0 ? needSag : needCrest)) { if (sign > 0) c[4]++; else c[5]++; }
                        }
                        sign = 0; sumA = 0f;
                    }
                    for (int i = 1; i + 1 < r.Count; i++)
                    {
                        float h0 = r[i].x - r[i - 1].x, h1 = r[i + 1].x - r[i].x;
                        float A = (r[i + 1].y - r[i].y) / h1 - (r[i].y - r[i - 1].y) / h0;
                        bool off = dev[i] || dev[i - 1] || dev[i + 1];
                        Count(e, A, h0, h1, r[i].x, false, off);
                        int sg = off || Mathf.Abs(A) < 0.0005f ? 0 : (A > 0f ? 1 : -1);
                        if (sg != sign) { Close(); if (sg != 0) { sign = sg; cStart = r[i].x - 0.5f * h0; } }
                        if (sign != 0) { sumA += A; cEnd = r[i].x + 0.5f * h1; }
                    }
                    Close();
                }
            }
            // through each two-arm node: the last drawn chord in, the first out
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var arms = map.nodeEdges[n];
                if (arms.Count != 2) continue;
                var A = map.edges[arms[0]]; var B = map.edges[arms[1]];
                if (A.a == A.b || B.a == B.b) continue;
                if (!EndChord(runs, A, n, out float gA, out float hA) || !EndChord(runs, B, n, out float gB, out float hB)) continue;
                // gA, gB: grades leaving the node along each arm
                var dA = A.a == n ? A.TangentAt(0f) : -A.TangentAt(A.length);
                var dB = B.a == n ? B.TangentAt(0f) : -B.TangentAt(B.length);
                if (Vector2.Dot(dA, dB) >= 0.3f) continue;
                float brk = gB + gA;   // in along A (grade -gA), out along B (gB)
                var lower = A.cls <= B.cls ? A : B;
                Count(lower, brk, hA, hB, lower == A ? (A.a == n ? 0f : A.length) : (B.a == n ? 0f : B.length), true, false);
            }

            // the routes: what a car feels
            foreach (var rt in map.routes)
            {
                var prof = new List<Vector2>();
                var vPost = new List<float>();
                float x0 = 0f;
                for (int k = 0; k < rt.edges.Length; k++)
                {
                    var e = map.edges[rt.edges[k]];
                    bool fwd = rt.dirs[k] >= 0;
                    if (!runs.TryGetValue(e.index, out var rl)) { x0 += e.length; continue; }
                    var all = new List<Vector2>();
                    foreach (var r in rl) all.AddRange(r);
                    all.Sort((p, q) => p.x.CompareTo(q.x));
                    if (!fwd) { all.Reverse(); for (int i = 0; i < all.Count; i++) all[i] = new Vector2(e.length - all[i].x, all[i].y); }
                    float post = e.speedKmh > 0 ? e.speedKmh / 3.6f : CityElevation.VerticalMph(e) * 0.44704f;
                    foreach (var p in all)
                    {
                        float x = x0 + p.x;
                        if (prof.Count > 0 && x - prof[prof.Count - 1].x < 0.05f) continue;
                        prof.Add(new Vector2(x, p.y)); vPost.Add(post);
                    }
                    x0 += e.length;
                }
                if (prof.Count < 3) { st.routeLines.Add($"{rt.id}: no drawn profile"); continue; }
                var a150 = new List<float>(); var aPost = new List<float>(); var ax = new List<float>();
                float GradeAt(float x)
                {
                    int lo = 0, hi = prof.Count - 2;
                    while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (prof[mid].x <= x) lo = mid; else hi = mid - 1; }
                    float dx = prof[lo + 1].x - prof[lo].x;
                    return dx > 1e-4f ? (prof[lo + 1].y - prof[lo].y) / dx : 0f;
                }
                int vi = 0;
                for (float x = prof[0].x + WheelbaseM; x < prof[prof.Count - 1].x - WheelbaseM; x += 0.5f)
                {
                    float k = (GradeAt(x + 0.5f * WheelbaseM) - GradeAt(x - 0.5f * WheelbaseM)) / WheelbaseM;
                    while (vi + 1 < prof.Count && prof[vi + 1].x <= x) vi++;
                    a150.Add(Mph150 * Mph150 * k / 9.81f); ax.Add(x);
                    aPost.Add(vPost[vi] * vPost[vi] * k / 9.81f);
                }
                int iMax = 0, iMin = 0;
                for (int i = 1; i < a150.Count; i++) { if (a150[i] > a150[iMax]) iMax = i; if (a150[i] < a150[iMin]) iMin = i; }
                string Where(float x)
                {
                    float acc = 0f;
                    for (int k = 0; k < rt.edges.Length; k++)
                    {
                        var e = map.edges[rt.edges[k]];
                        if (x <= acc + e.length) { float sl = rt.dirs[k] >= 0 ? x - acc : e.length - (x - acc); var p = e.PointAt(sl); return string.Format(CultureInfo.InvariantCulture, "e{0} s {1:0.0} ({2:0.0}, {3:0.0})", e.index, sl, p.x, p.y); }
                        acc += e.length;
                    }
                    return "-";
                }
                st.routeLines.Add($"{rt.id} '{rt.name}' {x0 / 1000f:0.0} km: 150 mph {Summ(a150)}; posted {Summ(aPost)}; worst sag at {(a150.Count > 0 ? Where(ax[iMax]) : "-")}, worst crest at {(a150.Count > 0 ? Where(ax[iMin]) : "-")}");
            }
            return st;
        }

        /// <summary>PSX_VS_SHOTS="x,z[,edge];...": each spot built (its 3 x 3
        /// tiles) and shot in the current state - a side view square to the
        /// road (orthographic, 8 m tall, the near verge clipped away) and a
        /// low chase view 30 m short of it looking along the road - into
        /// <paramref name="into"/>, two textures per spot.</summary>
        static void Shots(CityMap map, string spec, List<Texture2D> into)
        {
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var mats = PSXRacingBuilder.CityMaterials();
            Shader.SetGlobalFloat("_PSXFogNear", 900f); Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f)); Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            foreach (var part in spec.Split(';'))
            {
                var f = part.Split(',');
                if (f.Length < 2 || !float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                var at = new Vector2(x, z);
                int edge = f.Length > 2 && int.TryParse(f[2], out int ei) ? ei : -1;
                if (edge < 0 && map.NearestRoadPoint(at, 30f, false, out int ne, out _, out _)) edge = ne;
                if (edge < 0) continue;
                var e = map.edges[edge];
                CityElevation.ProjectOn(e, at, out float s);
                var world = new GameObject("~vsShots");
                int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize), ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var tm = CityMeshes.Build(map, trims, buildings, ptx + dx, ptz + dz);
                        var go = new GameObject("~tile"); go.transform.SetParent(world.transform, false); go.transform.position = tm.origin;
                        void Wrap(Mesh mesh, CityMeshes.Slot[] slots)
                        {
                            if (mesh == null || slots == null) return;
                            var g = new GameObject(mesh.name); g.transform.SetParent(go.transform, false);
                            g.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var use = new Material[slots.Length];
                            for (int i = 0; i < slots.Length; i++) use[i] = mats[(int)slots[i]];
                            g.AddComponent<MeshRenderer>().sharedMaterials = use;
                        }
                        Wrap(tm.ground, tm.groundSlots); Wrap(tm.roads, tm.roadSlots);
                        Wrap(tm.barriers, new[] { CityMeshes.Slot.Concrete }); Wrap(tm.kerbs, new[] { CityMeshes.Slot.Concrete });
                    }
                var t2 = e.TangentAt(s);
                var fwd = new Vector3(t2.x, 0f, t2.y); var right = new Vector3(t2.y, 0f, -t2.x);
                var p3 = new Vector3(at.x, e.YAt(s), at.y);
                float hw = e.HalfMax;
                // long lens: 0.7 m over the road 70 m short of the spot (the profile foreshortened)
                float s1 = Mathf.Clamp(s - 70f, 0f, e.length);
                var eyeL = new Vector3(e.PointAt(s1).x, e.YAt(s1) + 0.7f, e.PointAt(s1).y);
                into.Add(Render(eyeL, Quaternion.LookRotation(p3 + Vector3.up * 0.2f - eyeL), 0f, 960, 240, 0.3f, 14f));
                // chase: 1.1 m over the road 30 m short of the spot, looking at it
                float s0 = Mathf.Clamp(s - 30f, 0f, e.length);
                var eye = new Vector3(e.PointAt(s0).x, e.YAt(s0) + 1.1f, e.PointAt(s0).y);
                into.Add(Render(eye, Quaternion.LookRotation(p3 + Vector3.up * 0.3f - eye), 0f, 960, 540, 0.3f, 58f));
                UnityEngine.Object.DestroyImmediate(world);
            }
        }

        static Texture2D Render(Vector3 pos, Quaternion rot, float ortho, int w, int h, float near, float fov)
        {
            var camGO = new GameObject("~vsCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = near; cam.farClipPlane = 3000f; cam.fieldOfView = fov;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(camGO);
            return tex;
        }

        /// <summary>Per spot: BEFORE's side + chase over AFTER's, one PNG.</summary>
        static void SaveShots(string dir, List<Texture2D> before, List<Texture2D> after, Action<string> line)
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; i + 1 < before.Count && i + 1 < after.Count; i += 2)
            {
                var parts = new[] { before[i], before[i + 1], after[i], after[i + 1] };
                int W = 960, H = 2 * (240 + 540) + 8;
                var o = new Texture2D(W, H, TextureFormat.RGB24, false);
                var fill = new Color32[W * H]; for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(20, 20, 20, 255);
                o.SetPixels32(fill);
                int y = H;
                foreach (var t in parts) { y -= t.height; if (t == after[i]) y -= 8; o.SetPixels(0, y, t.width, t.height, t.GetPixels()); }
                o.Apply();
                string path = Path.Combine(dir, $"vsmooth_spot{i / 2 + 1}.png");
                File.WriteAllBytes(path, o.EncodeToPNG());
                line("shot: " + path + " (top: BEFORE long lens + chase; bottom: AFTER)");
                UnityEngine.Object.DestroyImmediate(o);
            }
        }

        /// <summary>How far apart in height a union's two decks stand, side by side.</summary>
        static string TwinDy(CityMap map)
        {
            var dys = new List<float>();
            if (map.deckPairs == null) return "-";
            foreach (var pr in map.deckPairs)
            {
                if (!pr.union) continue;
                var A = map.edges[pr.a]; var B = map.edges[pr.b];
                for (float s = Mathf.Min(pr.a0, pr.a1); s <= Mathf.Max(pr.a0, pr.a1); s += 2f)
                {
                    CityElevation.ProjectOn(B, A.PointAt(s), out float sb);
                    if (sb < Mathf.Min(pr.b0, pr.b1) - 0.5f || sb > Mathf.Max(pr.b0, pr.b1) + 0.5f) continue;
                    dys.Add(Mathf.Abs(A.YAt(s) - B.YAt(sb)));
                }
            }
            if (dys.Count == 0) return "-";
            dys.Sort();
            return string.Format(CultureInfo.InvariantCulture, "p50 {0:0.000} / p95 {1:0.000} / max {2:0.000} m over {3}", dys[dys.Count / 2], dys[(int)(0.95f * (dys.Count - 1))], dys[dys.Count - 1], dys.Count);
        }

        static string Summ(List<float> a)
        {
            if (a.Count == 0) return "-";
            var abs = new List<float>(a.Count); float mx = 0f, sag = 0f, crest = 0f;
            foreach (var v in a) { abs.Add(Mathf.Abs(v)); mx = Mathf.Max(mx, Mathf.Abs(v)); sag = Mathf.Max(sag, v); crest = Mathf.Max(crest, -v); }
            abs.Sort();
            float p95 = abs[Mathf.Min(abs.Count - 1, (int)(0.95f * abs.Count))];
            float p99 = abs[Mathf.Min(abs.Count - 1, (int)(0.99f * abs.Count))];
            return string.Format(CultureInfo.InvariantCulture, "max {0:0.00} / p95 {1:0.000} / p99 {2:0.00} / sag {3:0.00} / crest {4:0.00}", mx, p95, p99, sag, crest);
        }

        /// <summary>The drawn chord at the node end of an edge: its grade
        /// leaving the node and its length.</summary>
        static bool EndChord(Dictionary<int, List<List<Vector2>>> runs, CityMap.Edge e, int node, out float g, out float h)
        {
            g = 0f; h = 0f;
            if (!runs.TryGetValue(e.index, out var rl)) return false;
            bool atA = e.a == node;
            foreach (var r in rl)
            {
                if (r.Count < 2) continue;
                if (atA && r[0].x < 0.05f) { h = r[1].x - r[0].x; g = (r[1].y - r[0].y) / h; return h > 0.05f; }
                if (!atA && r[r.Count - 1].x > e.length - 0.05f) { int m = r.Count - 1; h = r[m].x - r[m - 1].x; g = (r[m - 1].y - r[m].y) / h; return h > 0.05f; }
            }
            return false;
        }

        static float SideY(ushort flags, float yL, float yR)
        {
            if ((flags & CityMeshes.RoadTap.FClipL) != 0) return yR;
            if ((flags & CityMeshes.RoadTap.FClipR) != 0) return yL;
            return 0.5f * (yL + yR);
        }
    }
}
