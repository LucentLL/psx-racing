using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// REAL HEIGHTS round 10 (2026-10-29): the probe and the look A/B.
    ///
    ///   Probe   -executeMethod PSXRacing.EditorTools.CityR10.Probe: one solve (switches as set);
    ///           the structure flags of PSX_R10_TRACE's edges after each structure step, the one
    ///           abutment line's carries (which ran past the twin's own end), and every point of
    ///           PSX_R10_POINTS ("label:x:z" a ray down: every collider met, the land's terms, the
    ///           roads near and whose drawn ribbon covers it; "label:x:z:y" a face probe at that
    ///           height: colliders within 0.6 m and short rays at it from six ways). -> r10_probe.txt
    ///   LookAB  -executeMethod PSXRacing.EditorTools.CityR10.LookAB: two passes, round 10 OFF then
    ///           ON (CityElevation + CityMeshes round-10 switches), each: solve, the PSX_SPOTS plans
    ///           (spot_probe_off/_on.txt), the points (r10_probe_off/_on.txt) and the
    ///           PSX_PREVIEW_CUSTOM views (Screenshots/City/name_off/_on.png).
    ///           PSX_LOOK_PASSES=off|on|both.
    /// </summary>
    public static class CityR10
    {
        static string Root => Directory.GetParent(Application.dataPath).FullName;

        public static void Probe()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[R10] no city data"); return; }
            CityElevation.Solve(map);
            ResetWorldTrims();
            File.WriteAllText(Path.Combine(Root, "r10_probe.txt"), ProbeText(map, "as set"));
            Debug.Log("[R10] probe done");
        }

        public static void LookAB()
        {
            string shots = Path.Combine(Root, "Screenshots", "City");
            var names = new List<string>();
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_PREVIEW_CUSTOM") ?? "").Split(';'))
                if (item.Contains(":")) names.Add(item.Split(':')[0].Trim());
            string passes = System.Environment.GetEnvironmentVariable("PSX_LOOK_PASSES") ?? "both";
            var map = CityMap.Get();
            if (map == null) return;
            var keepE = CityElevation.Round10Flags();
            var keepM = CityMeshes.Round10Flags();
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    if ((on && passes == "off") || (!on && passes == "on")) continue;
                    SetRound10(on, keepE, keepM);
                    CityElevation.Solve(map);
                    ResetWorldTrims();
                    string sfx = on ? "_on" : "_off";
                    try { File.WriteAllText(Path.Combine(Root, "r10_probe" + sfx + ".txt"), ProbeText(map, on ? "round 10 ON" : "round 10 OFF")); }
                    catch (System.Exception ex) { Debug.LogWarning("[R10] probe: " + ex); }
                    ResetWorldTrims();
                    if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PSX_SPOTS")))
                        try
                        {
                            MedianProfileProbe.Spots();
                            string sp = Path.Combine(Root, "spot_probe.txt");
                            if (File.Exists(sp)) File.Copy(sp, Path.Combine(Root, "spot_probe" + sfx + ".txt"), true);
                        }
                        catch (System.Exception ex) { Debug.LogWarning("[R10] spot probe: " + ex.Message); }
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
            finally { CityElevation.SetRound10Flags(keepE); CityMeshes.SetRound10Flags(keepM); ResetWorldTrims(); }
            Debug.Log("[R10] look A/B done");
        }

        /// <summary>Round 10's switches all ON (as set by the environment, each =0 kept off) or all OFF.</summary>
        static void SetRound10(bool on, bool[] keepE, bool[] keepM)
        {
            var e = (bool[])keepE.Clone(); var m = (bool[])keepM.Clone();
            for (int i = 0; i < e.Length; i++) e[i] = on && keepE[i];
            for (int i = 0; i < m.Length; i++) m[i] = on && keepM[i];
            CityElevation.SetRound10Flags(e); CityMeshes.SetRound10Flags(m);
        }

        /// <summary>Forget CityWorld's cached trims (the union runs), so the next world built
        /// computes them with the switches as now set.</summary>
        static void ResetWorldTrims()
        {
            var f = typeof(CityWorld).GetField("cachedFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) f.SetValue(null, null);
            else Debug.LogWarning("[R10] CityWorld.cachedFor not found");
        }

        static string ProbeText(CityMap map, string label)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"R10 PROBE ({label})");
            sb.AppendLine($"one abutment line: {CityElevation.AbutmentEnds} ends carried on {CityElevation.AbutmentPairs} pairs (max {CityElevation.AbutmentMaxM:0.0} m); round 10: {CityElevation.R10AbutLimited} carries stopped at the twin's own end (the line up to {CityElevation.R10AbutLimitMaxM:0.0} m on), {CityElevation.R10AbutPastFoot} carries that run (or would run) past the twin's end");
            sb.AppendLine("  carries:" + CityElevation.R10AbutLog);
            sb.AppendLine("structure trace (PSX_R10_TRACE):");
            sb.Append(CityElevation.R10Trace);
            var pts = (System.Environment.GetEnvironmentVariable("PSX_R10_POINTS") ?? "").Split(';');
            var hits = new RaycastHit[32];
            var secs = new List<CityMeshes.AuditView.SectionView>(256);
            foreach (var item in pts)
            {
                var f = item.Split(':');
                if (f.Length < 3) continue;
                string name = f[0].Trim();
                float x = float.Parse(f[1], ci), z = float.Parse(f[2], ci);
                bool face = f.Length >= 4;
                float fy = face ? float.Parse(f[3], ci) : 0f;
                var go = new GameObject("~r10Probe");
                var world = go.AddComponent<CityWorld>();
                try
                {
                    CityMeshes.AuditView.BeginRecord();
                    // (a face point) every closing face / fascia the tiles lay, to name the one standing here
                    if (face) CityMeshes.skirtLog = new List<(Vector3 at, string what)>(4096);
                    world.EnsureRing(new Vector3(x, 100f, z), 1);
                    CityMeshes.AuditView.EndRecord();
                    Physics.SyncTransforms();
                    if (face && CityMeshes.skirtLog != null)
                    {
                        int shown = 0;
                        foreach (var (at, what) in CityMeshes.skirtLog)
                        {
                            if (new Vector2(at.x - x, at.z - z).magnitude > 4f || shown >= 24) continue;
                            sb.AppendLine($"  skirt near ({at.x:0.00},{at.y:0.00},{at.z:0.00}): {what}");
                            shown++;
                        }
                        sb.AppendLine($"  ({CityMeshes.skirtLog.Count} closing faces logged on the ring, {shown} within 4 m)");
                        CityMeshes.skirtLog = null;
                    }
                    var p = new Vector2(x, z);
                    float g = CityElevation.Ground(map, x, z, out var gt);
                    sb.AppendLine($"POINT {name} ({x:0.00},{z:0.00}){(face ? $" y {fy:0.00}" : "")}: land {g:0.00}, lattice {CityMeshes.LatticeAt(map, x, z):0.00} (dem {gt.dem:0.00} floor {gt.floor:0.00} e{gt.floorEdge} cut {gt.cut:0.00} cap {gt.protect:0.00} e{gt.protectEdge} deckCap {gt.deckCap:0.00} deckProt {gt.deckProtect:0.00} e{gt.deckEdge} channel {gt.channel:0.00})");
                    // (a label starting "L") the 8 m lattice corners round the point, 7 x 7: each corner's
                    // ground against the point's land, and the term that set it (d deck cap, D deck protect,
                    // p a road's cap, f a fill floor, c a cut, n the land; s marks a round-10 spill floor)
                    if (name.StartsWith("L"))
                    {
                        int cx = Mathf.FloorToInt(x / 8f), cz = Mathf.FloorToInt(z / 8f);
                        sb.AppendLine($"  lattice corners (x {(cx - 3) * 8}..{(cx + 3) * 8}, rows z from {(cz + 3) * 8} down to {(cz - 3) * 8}), metres against {g:0.00}:");
                        for (int jz = cz + 3; jz >= cz - 3; jz--)
                        {
                            var row = new StringBuilder($"    z {jz * 8,6}:");
                            for (int jx = cx - 3; jx <= cx + 3; jx++)
                            {
                                float gy = CityElevation.Ground(map, jx * 8f, jz * 8f, out var ct);
                                bool Eq(float v) => !float.IsNaN(v) && Mathf.Abs(v - gy) < 0.005f;
                                char t = Eq(ct.deckCap) ? 'd' : Eq(ct.deckProtect) ? 'D' : Eq(ct.protect) ? 'p' : Eq(ct.floor) ? 'f' : Eq(ct.cut) ? 'c' : 'n';
                                row.Append($" {gy - g,6:+0.00;-0.00}{t}");
                            }
                            sb.AppendLine(row.ToString());
                        }
                    }
                    if (!face)
                    {
                        var all = Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore);
                        System.Array.Sort(all, (a, b) => b.point.y.CompareTo(a.point.y));
                        sb.AppendLine($"  ray down: {all.Length} hits");
                        foreach (var h in all) sb.AppendLine($"    {Path3(h.collider.transform)} [{h.collider.GetType().Name}] y {h.point.y:0.00} n ({h.normal.x:0.00},{h.normal.y:0.00},{h.normal.z:0.00}) tri {h.triangleIndex}");
                    }
                    else
                    {
                        var c = new Vector3(x, fy, z);
                        var near = Physics.OverlapSphere(c, 0.6f, ~0, QueryTriggerInteraction.Ignore);
                        sb.AppendLine($"  colliders within 0.6 m: {near.Length}");
                        foreach (var col in near)
                        {
                            var cp = col is MeshCollider mc && !mc.convex ? c : col.ClosestPoint(c);
                            sb.AppendLine($"    {Path3(col.transform)} [{col.GetType().Name}] bounds y {col.bounds.min.y:0.00}..{col.bounds.max.y:0.00}");
                        }
                        foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
                        {
                            var from = c - dir * 1.5f;
                            int n = Physics.RaycastNonAlloc(from, dir, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
                            var line = new StringBuilder($"    ray {dir}:");
                            for (int i = 0; i < n; i++)
                                line.Append($" {Path3(hits[i].collider.transform)}@{hits[i].distance - 1.5f:+0.00;-0.00} n({hits[i].normal.x:0.00},{hits[i].normal.y:0.00},{hits[i].normal.z:0.00}) y {hits[i].point.y:0.00};");
                            sb.AppendLine(line.ToString());
                        }
                    }
                    // the roads near, and whose drawn ribbon covers the point
                    var segs = new HashSet<int>();
                    map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, segs);
                    var seen = new HashSet<int>();
                    foreach (int packed in segs)
                    {
                        int oi = packed >> 12;
                        if (!seen.Add(oi)) continue;
                        var o = map.edges[oi];
                        CityElevation.ProjectOn(o, p, out float at);
                        var q = o.PointAt(at);
                        var t = o.TangentAt(at);
                        float lat = Vector2.Dot(p - q, new Vector2(-t.y, t.x));
                        float d = Vector2.Distance(p, q);
                        if (d > 25f) continue;
                        CityMeshes.AuditView.SectionsOf(oi, secs);
                        string cover = "";
                        for (int j = 0; j + 1 < secs.Count; j++)
                        {
                            if (secs[j + 1].s < at - 0.05f || secs[j].s > at + 0.05f) continue;
                            var A = secs[j]; var B = secs[j + 1];
                            float u = B.s - A.s > 1e-5f ? Mathf.Clamp01((at - A.s) / (B.s - A.s)) : 0f;
                            float lo = Mathf.Lerp(A.LatL, B.LatL, u), hi = Mathf.Lerp(A.LatR, B.LatR, u);
                            float yy = Mathf.Lerp(0.5f * (A.L.y + A.R.y), 0.5f * (B.L.y + B.R.y), u);
                            cover = $" drawn lat {lo:+0.00;-0.00}..{hi:+0.00;-0.00} y {yy:0.00}{(A.elev ? " E" : "")}{(A.collapsed ? " COL" : "")}{(A.clippedIn ? " clip" : "")}";
                            break;
                        }
                        sb.AppendLine($"    e{oi} '{o.name}'{(o.link ? " L" : "")}{(o.bridge ? " B" : "")} cls{o.cls} hw {o.HalfMax:0.0} s {at:0.0}/{o.length:0.0} d {d:0.0} lat {lat:+0.0;-0.0} y {o.YAt(at):0.00}{(o.ElevatedAt(at) ? " ELEV" : "")}{cover}");
                    }
                }
                catch (System.Exception ex) { sb.AppendLine($"POINT {name}: {ex.Message}"); }
                finally { Object.DestroyImmediate(go); }
            }
            // (after the first tiles: the union runs are built with the first world)
            sb.AppendLine("unions: " + CityMeshes.LastUnionReport);
            sb.AppendLine("  deck V ends closed (round 10):" + CityMeshes.R10DeckVLog);
            return sb.ToString();
        }

        static string Path3(Transform t)
        {
            string s = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
