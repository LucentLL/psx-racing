using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// REAL HEIGHTS round 11 (2026-10-30): the probe.
    ///
    ///   Probe  -executeMethod PSXRacing.EditorTools.CityR11.Probe: one solve (switches as set), then
    ///          PSX_R11_FACES="label:x:y:z[:r];..." - every Roads and Ground triangle of the 3 x 3
    ///          tiles round the point within r (default 0.3 m, 3D) of it, with its vertices, its normal
    ///          and the tile tap's record it belongs to (a ribbon span: edge and arcs; a fan: node; a
    ///          gore: host), so a face the audit names can be traced to what drew it;
    ///          PSX_R11_RAILTRACE=edge with PSX_R11_RAILAT="x:z": the rail-end plan's view of that
    ///          edge on the tiles round the point (CityMeshes.R11RailTrace) and its drawn sections;
    ///          PSX_R11_EDGES="e1,e2": the edges' stations and drawn sections. -> r11_probe.txt
    /// </summary>
    public static class CityR11
    {
        static string Root => Directory.GetParent(Application.dataPath).FullName;
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        /// <summary>The look A/B (-executeMethod PSXRacing.EditorTools.CityR11.LookAB): two passes, round 11
        /// OFF then ON (CityElevation + CityMeshes round-11 switches; each =0 in the environment stays
        /// off), each: solve, the PSX_SPOTS plans (spot_probe_off/_on.txt), the probe (r11_probe_off/_on.txt)
        /// and the PSX_PREVIEW_CUSTOM views (Screenshots/City/name_off/_on.png). PSX_LOOK_PASSES=off|on|both.</summary>
        public static void LookAB()
        {
            string shots = Path.Combine(Root, "Screenshots", "City");
            var names = new List<string>();
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_PREVIEW_CUSTOM") ?? "").Split(';'))
                if (item.Contains(":")) names.Add(item.Split(':')[0].Trim());
            string passes = System.Environment.GetEnvironmentVariable("PSX_LOOK_PASSES") ?? "both";
            var map = CityMap.Get();
            if (map == null) return;
            var keepE = CityElevation.Round11Flags();
            var keepM = CityMeshes.Round11Flags();
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    if ((on && passes == "off") || (!on && passes == "on")) continue;
                    var e = (bool[])keepE.Clone(); var m = (bool[])keepM.Clone();
                    for (int i = 0; i < e.Length; i++) e[i] = on && keepE[i];
                    for (int i = 0; i < m.Length; i++) m[i] = on && keepM[i];
                    CityElevation.SetRound11Flags(e); CityMeshes.SetRound11Flags(m);
                    CityElevation.Solve(map);
                    ResetWorldTrims();
                    string sfx = on ? "_on" : "_off";
                    if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PSX_SPOTS")))
                        try
                        {
                            MedianProfileProbe.Spots();
                            string sp = Path.Combine(Root, "spot_probe.txt");
                            if (File.Exists(sp)) File.Copy(sp, Path.Combine(Root, "spot_probe" + sfx + ".txt"), true);
                        }
                        catch (System.Exception ex) { Debug.LogWarning("[R11] spot probe: " + ex.Message); }
                    try
                    {
                        var sb = new StringBuilder();
                        var trims = CityMeshes.NodeTrims(map);
                        var buildings = CityBuildings.Precompute(map);
                        sb.AppendLine($"R11 PROBE ({(on ? "round 11 ON" : "round 11 OFF")})");
                        Faces(map, trims, buildings, sb);
                        RailTrace(map, trims, buildings, sb);
                        File.WriteAllText(Path.Combine(Root, "r11_probe" + sfx + ".txt"), sb.ToString());
                    }
                    catch (System.Exception ex) { Debug.LogWarning("[R11] probe: " + ex); }
                    ResetWorldTrims();
                    var t0 = System.DateTime.Now.AddSeconds(-2);
                    if (names.Count > 0)
                    {
                        CityPreview.Run();
                        if (Directory.Exists(shots))
                            foreach (var f in Directory.GetFiles(shots, "*.png"))
                            {
                                string b = Path.GetFileNameWithoutExtension(f);
                                if (b.EndsWith("_off") || b.EndsWith("_on") || File.GetLastWriteTime(f) < t0 || !names.Contains(b)) continue;
                                File.Copy(f, Path.Combine(shots, b + sfx + ".png"), true);
                            }
                    }
                }
            }
            finally { CityElevation.SetRound11Flags(keepE); CityMeshes.SetRound11Flags(keepM); ResetWorldTrims(); }
            Debug.Log("[R11] look A/B done");
        }

        /// <summary>The launch audit twice on one load (-executeMethod PSXRacing.EditorTools.CityR11.LaunchAB):
        /// round 11 OFF then ON (each re-solved), city_launch_off/_on.txt|csv|json, then the probe (as set).
        /// PSX_LOOK_PASSES=off|on|both. The audit's own environment as set (PSX_LAUNCH_UNIFORM=1 = the gate's
        /// "after" sampling; PSX_LAUNCH_EXPLAIN).</summary>
        public static void LaunchAB()
        {
            string passes = System.Environment.GetEnvironmentVariable("PSX_LOOK_PASSES") ?? "both";
            var map = CityMap.Get();
            if (map == null) return;
            var keepE = CityElevation.Round11Flags();
            var keepM = CityMeshes.Round11Flags();
            CityLaunchAudit.NoExit = true;
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    if ((on && passes == "off") || (!on && passes == "on")) continue;
                    var e = (bool[])keepE.Clone(); var m = (bool[])keepM.Clone();
                    for (int i = 0; i < e.Length; i++) e[i] = on && keepE[i];
                    for (int i = 0; i < m.Length; i++) m[i] = on && keepM[i];
                    CityElevation.SetRound11Flags(e); CityMeshes.SetRound11Flags(m);
                    CityElevation.KeepTerrainProfiles = true;
                    CityElevation.Solve(map);
                    CityElevation.KeepTerrainProfiles = false;
                    ResetWorldTrims();
                    string sfx = on ? "on" : "off";
                    System.Environment.SetEnvironmentVariable("PSX_LAUNCH_LABEL", "r11_" + sfx);
                    try { CityLaunchAudit.Run(); } catch (System.Exception ex) { Debug.LogException(ex); }
                    foreach (var f in new[] { "city_launch.txt", "city_launch.csv", "city_launch.json" })
                    {
                        var src = Path.Combine(Root, f);
                        if (File.Exists(src)) File.Copy(src, Path.Combine(Root, Path.GetFileNameWithoutExtension(f) + "_" + sfx + Path.GetExtension(f)), true);
                    }
                }
            }
            finally { CityLaunchAudit.NoExit = false; CityElevation.SetRound11Flags(keepE); CityMeshes.SetRound11Flags(keepM); ResetWorldTrims(); }
            if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PSX_R10_POINTS")))
                try { CityR10.Probe(); } catch (System.Exception ex) { Debug.LogException(ex); }   // (it solves, as set)
            else CityElevation.Solve(map);
            ResetWorldTrims();
            Probe();
        }

        /// <summary>Forget CityWorld's cached trims (the union runs), so the next world built
        /// computes them with the switches as now set.</summary>
        static void ResetWorldTrims()
        {
            var f = typeof(CityWorld).GetField("cachedFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) f.SetValue(null, null);
        }

        /// <summary>The launch audit (as the environment sets it: PSX_LAUNCH_UNIFORM=1 is the gate's
        /// "after" sampling, PSX_LAUNCH_EXPLAIN=1 every spot's surfaces), then the probe, on one solve.</summary>
        public static void LaunchAndProbe()
        {
            CityLaunchAudit.NoExit = true;
            try { CityLaunchAudit.Run(); }
            catch (System.Exception ex) { Debug.LogException(ex); }
            finally { CityLaunchAudit.NoExit = false; }
            // round 10's point probe too (PSX_R10_POINTS: "L..." labels print the 8 m lattice round a point)
            if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PSX_R10_POINTS")))
                try { CityR10.Probe(); } catch (System.Exception ex) { Debug.LogException(ex); }
            Probe();
        }

        public static void Probe()
        {
            var sb = new StringBuilder();
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new System.Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                sb.AppendLine($"R11 PROBE - solve {CityMap.LastSolveMs:0} ms");
                Faces(map, trims, buildings, sb);
                RailTrace(map, trims, buildings, sb);
                // PSX_R11_AT="edge:s:lat;...": the world point at arc s, lat m to the LEFT of the edge's travel
                // (+ = left), its land (with the term that set it) and the drawn 8 m lattice there
                foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_R11_AT") ?? "").Split(';'))
                {
                    var f = t.Split(':');
                    if (f.Length < 3 || !int.TryParse(f[0].Trim().TrimStart('e'), out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                    var e = map.edges[ei];
                    float s = float.Parse(f[1], ci), lat = float.Parse(f[2], ci);
                    var tn = e.TangentAt(s);
                    var p = e.PointAt(s) + new Vector2(-tn.y, tn.x) * lat;
                    float g = CityElevation.Ground(map, p.x, p.y, out var gt);
                    sb.AppendLine($"AT e{ei} s {s:0.0} lat {lat:+0.0;-0.0}: ({p.x:0.00},{p.y:0.00}) road y {e.YAt(s):0.00}{(e.ElevatedAt(s) ? " ELEV" : "")} land {g:0.00} lattice {CityMeshes.LatticeAt(map, p.x, p.y):0.00} (dem {gt.dem:0.00} floor {gt.floor:0.00} e{gt.floorEdge} cut {gt.cut:0.00} cap {gt.protect:0.00} e{gt.protectEdge} deckCap {gt.deckCap:0.00} deckProt {gt.deckProtect:0.00} e{gt.deckEdge} channel {gt.channel:0.00})");
                }
                foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_R11_EDGES") ?? "").Split(','))
                {
                    if (!int.TryParse(t.Trim().TrimStart('e'), out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                    var e = map.edges[ei];
                    var st = new StringBuilder();
                    for (int i = 0; i < e.stS.Length; i++) st.Append($" {e.stS[i]:0.0}:{e.stY[i]:0.00}{(e.stElev[i] ? "d" : "")}{(e.SeatedAt(i) ? "s" : "")}");
                    sb.AppendLine($"EDGE e{ei} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} len {e.length:0.0} w {e.width:0.0} oneway {e.oneway} a=n{e.a} b=n{e.b} trims {trims.atA[ei]:0.0}/{trims.atB[ei]:0.0} branch a e{trims.branchA[ei]} b e{trims.branchB[ei]}");
                    sb.AppendLine("  stations" + st);
                    sb.Append(CityMeshes.DescribeSections(map, trims, e));
                    sb.AppendLine("  node a: " + CityMeshes.DescribeNode(map, trims, e.a));
                    sb.AppendLine("  node b: " + CityMeshes.DescribeNode(map, trims, e.b));
                }
            }
            catch (System.Exception ex) { sb.AppendLine("THREW " + ex); Debug.LogException(ex); }
            File.WriteAllText(Path.Combine(Root, "r11_probe.txt"), sb.ToString());
            Debug.Log("[R11] probe done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---- faces: which drawn triangle stands at a point, and what drew it -------------
        static void Faces(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, StringBuilder sb)
        {
            string spec = System.Environment.GetEnvironmentVariable("PSX_R11_FACES");
            if (string.IsNullOrEmpty(spec)) return;
            var tiles = new Dictionary<long, CityMeshes.TileMeshes>();
            CityMeshes.RecordTap = true;
            try
            {
                foreach (var item in spec.Split(';'))
                {
                    var f = item.Split(':');
                    if (f.Length < 4) continue;
                    string label = f[0].Trim();
                    var c = new Vector3(float.Parse(f[1], ci), float.Parse(f[2], ci), float.Parse(f[3], ci));
                    float r = f.Length >= 5 ? float.Parse(f[4], ci) : 0.3f;
                    sb.AppendLine($"FACES {label} at ({c.x:0.00},{c.y:0.00},{c.z:0.00}) within {r:0.00} m:");
                    int tx = Mathf.FloorToInt(c.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(c.z / CityMeshes.TileSize);
                    int found = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long key = ((long)(tz + dz) << 32) | (uint)(tx + dx + 100000);
                            if (!tiles.TryGetValue(key, out var tm)) tiles[key] = tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                            found += MeshFaces(tm, tm.roads, "Roads", true, c, r, sb);
                            found += MeshFaces(tm, tm.ground, "Ground", false, c, r, sb);
                            found += MeshFaces(tm, tm.barriers, "Barriers", false, c, r, sb);
                            found += MeshFaces(tm, tm.kerbs, "Kerbs", false, c, r, sb);
                        }
                    sb.AppendLine($"  {found} triangles");
                    // the roads near
                    var p = new Vector2(c.x, c.z);
                    var near = new HashSet<int>();
                    map.EdgeSegsInRect(p - Vector2.one * 20f, p + Vector2.one * 20f, near);
                    var seen = new HashSet<int>();
                    foreach (int packed in near)
                    {
                        int oi = packed >> 12;
                        if (!seen.Add(oi)) continue;
                        var o = map.edges[oi];
                        CityElevation.ProjectOn(o, p, out float so);
                        float d = Vector2.Distance(o.PointAt(so), p);
                        if (d > 20f) continue;
                        var tn = o.TangentAt(so);
                        float lat = Vector2.Dot(p - o.PointAt(so), new Vector2(-tn.y, tn.x));
                        sb.AppendLine($"    e{oi} '{o.name}'{(o.link ? " L" : "")}{(o.bridge ? " B" : "")} cls{o.cls} hw {o.HalfMax:0.0} s {so:0.0}/{o.length:0.0} d {d:0.0} lat {lat:+0.0;-0.0} y {o.YAt(so):0.00}{(o.ElevatedAt(so) ? " ELEV" : "")} a=n{o.a} b=n{o.b} trims {trims.atA[oi]:0.0}/{trims.atB[oi]:0.0}");
                    }
                    if (tiles.Count > 120) { foreach (var tm in tiles.Values) Destroy(tm); tiles.Clear(); }
                }
            }
            finally
            {
                CityMeshes.RecordTap = false;
                foreach (var tm in tiles.Values) Destroy(tm);
            }
        }

        static int MeshFaces(CityMeshes.TileMeshes tm, Mesh m, string name, bool tapped, Vector3 c, float r, StringBuilder sb)
        {
            if (m == null || m.vertexCount == 0) return 0;
            var v = m.vertices;
            int found = 0;
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                var t = m.GetTriangles(sub);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]] + tm.origin, b = v[t[i + 1]] + tm.origin, cc = v[t[i + 2]] + tm.origin;
                    var q = ClosestOnTriangle(c, a, b, cc);
                    if ((q - c).sqrMagnitude > r * r) continue;
                    var nrm = Vector3.Cross(b - a, cc - a);
                    float area = nrm.magnitude * 0.5f;
                    nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.zero;
                    string who = tapped ? Who(tm, sub, i, t[i]) : "";
                    string slot = tapped && tm.roadSlots != null && sub < tm.roadSlots.Length ? tm.roadSlots[sub].ToString() : "sub" + sub;
                    sb.AppendLine(string.Format(ci, "  {0} tile {1:0},{2:0} {3} tri {4} v{5}: ({6:0.00},{7:0.00},{8:0.00}) ({9:0.00},{10:0.00},{11:0.00}) ({12:0.00},{13:0.00},{14:0.00}) n ({15:0.00},{16:0.00},{17:0.00}) area {18:0.00} at {19:0.00} m |{20}",
                        name, tm.origin.x / CityMeshes.TileSize, tm.origin.z / CityMeshes.TileSize, slot, i / 3, t[i], a.x, a.y, a.z, b.x, b.y, b.z, cc.x, cc.y, cc.z, nrm.x, nrm.y, nrm.z, area, (q - c).magnitude, who));
                    if (++found >= 40) return found;
                }
            }
            return found;
        }

        /// <summary>The tap record a Roads triangle belongs to.</summary>
        static string Who(CityMeshes.TileMeshes tm, int sub, int triIndex, int v0)
        {
            var tap = tm.tap;
            if (tap == null || tap.slotBase == null || tm.roadSlots == null) return " (no tap)";
            int slot = (int)tm.roadSlots[sub];
            int baseV = tap.slotBase[sub];
            foreach (var fa in tap.fans)
                if (fa.slot == slot && triIndex >= fa.triStart && triIndex < fa.triStart + 3 * fa.triCount) return $" fan n{fa.node}";
            foreach (var sp in tap.spans)
            {
                if (sp.slot != slot) continue;
                int lo = baseV + sp.bucketV, hi = lo + 4 * System.Math.Max(1, sp.strips);
                if (v0 >= lo && v0 < hi) return string.Format(ci, " span e{0} s {1:0.00}..{2:0.00} flags {3}/{4}", sp.edge, sp.sA, sp.sB, sp.flagsA, sp.flagsB);
            }
            foreach (var g in tap.gores)
            {
                if (g.slot != slot) continue;
                int lo = baseV + g.bucketV, hi = lo + 4 * System.Math.Max(1, g.strips);
                if (v0 >= lo && v0 < hi) return string.Format(ci, " gore host e{0} s {1:0.00}..{2:0.00}", g.edge, g.sA, g.sB);
            }
            return " untapped";
        }

        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float den = 1f / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }

        // ---- the rail-end plan on one edge ----------------------------------------------
        static void RailTrace(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, StringBuilder sb)
        {
            int ei = CityMeshes.R11RailTraceEdge;
            string at = System.Environment.GetEnvironmentVariable("PSX_R11_RAILAT");
            if (ei < 0 || string.IsNullOrEmpty(at)) return;
            var f = at.Split(':');
            float x = float.Parse(f[0], ci), z = float.Parse(f[1], ci);
            int tx = Mathf.FloorToInt(x / CityMeshes.TileSize), tz = Mathf.FloorToInt(z / CityMeshes.TileSize);
            CityMeshes.R11RailTrace.Clear();
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                    Destroy(tm);
                }
            sb.AppendLine($"RAIL TRACE e{ei} '{map.edges[ei].name}' on the tiles round ({x:0},{z:0}): {CityMeshes.R11RailTrace.Count} kind changes (spans: D decided, E elev, W wedge, S skip, A approach; r rail, c cut, m median, g gap, u union, t retain)");
            foreach (var l in CityMeshes.R11RailTrace) sb.AppendLine("  " + l);
            sb.Append(CityMeshes.DescribeSections(map, trims, map.edges[ei]));
        }

        static void Destroy(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.buildings, tm.lampPosts })
                if (m != null) Object.DestroyImmediate(m);
        }
    }
}
