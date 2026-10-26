using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// REAL HEIGHTS (2026-10-25): one job, the city solved twice - with the
    /// measured profiles (RPRF on: every road on its 3DEP ground) and without
    /// (the smoothed-land solve as main ships it) - and for each: every
    /// station dumped (real_on.tsv / real_off.tsv: edge, i, s, station y,
    /// drawn y, structure), the census (real_on.txt / real_off.txt: the
    /// solve's REAL report, grades past the class limit by tier, clearance
    /// under every enforced crossing), the PROFILE report
    /// (CityAudit.HeightsOnly -> heights_on/off.txt) and the exit 3A box
    /// (CityDeckFacts -> deck_facts_on/off.txt). tools/city/realheights_check.mjs
    /// reads the dumps against 3DEP. Headless:
    /// -executeMethod PSXRacing.EditorTools.CityRealHeights.Run
    /// </summary>
    public static class CityRealHeights
    {
        public static void Run()
        {
            string dir = Directory.GetParent(Application.dataPath).FullName;
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText(Path.Combine(dir, "real_on.txt"), "charlotte_city.bytes missing"); return; }
            foreach (bool on in new[] { true, false })
            {
                CityElevation.RprfOverride = on;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                CityElevation.Solve(map);
                long ms = clock.ElapsedMilliseconds;
                string tag = on ? "on" : "off";
                Dump(map, Path.Combine(dir, $"real_{tag}.tsv"));
                File.WriteAllText(Path.Combine(dir, $"real_{tag}.txt"), Census(map, ms));
                try { CityAudit.HeightsOnly(); } catch (System.Exception ex) { Debug.LogError("[RealHeights] HeightsOnly threw " + ex); }
                try
                {
                    CityDeckFacts.Run();
                    if (File.Exists("deck_facts.txt")) File.Copy("deck_facts.txt", Path.Combine(dir, $"deck_facts_{tag}.txt"), true);
                }
                catch (System.Exception ex) { Debug.LogError("[RealHeights] DeckFacts threw " + ex); }
                Debug.Log($"[RealHeights] {tag}: solve {ms} ms; {CityElevation.RealReport}");
            }
            CityElevation.RprfOverride = null;
        }

        /// <summary>The census both ways, then the shots both ways, in one job.</summary>
        public static void RunAll()
        {
            Run();
            try { RunShots(); } catch (System.Exception ex) { Debug.LogError("[RealHeights] shots threw " + ex); }
        }

        /// <summary>The owner's spots, BEFORE (RPRF off) and AFTER (on): the
        /// PSX_PREVIEW_CUSTOM views (CityPreview, PSX_PREVIEW_SPOTS=custom),
        /// each PNG renamed *_before / *_after in Screenshots/City. Headless:
        /// -executeMethod PSXRacing.EditorTools.CityRealHeights.RunShots</summary>
        public static void RunShots()
        {
            string shots = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City");
            var names = new System.Collections.Generic.List<string>();
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_PREVIEW_CUSTOM") ?? "").Split(';'))
                if (item.Contains(":")) names.Add(item.Split(':')[0].Trim());
            var map = CityMap.Get();
            if (map == null) return;
            foreach (bool on in new[] { false, true })
            {
                CityElevation.RprfOverride = on;
                CityElevation.Solve(map);
                var t0 = System.DateTime.Now.AddSeconds(-2);
                CityPreview.Run();
                if (!Directory.Exists(shots)) continue;
                foreach (var f in Directory.GetFiles(shots, "*.png"))
                {
                    string b = Path.GetFileNameWithoutExtension(f);
                    if (b.EndsWith("_before") || b.EndsWith("_after") || File.GetLastWriteTime(f) < t0) continue;
                    bool mine = false; foreach (var n in names) if (b.Contains(n)) { mine = true; break; }
                    if (!mine) continue;
                    string to = Path.Combine(shots, b + (on ? "_after" : "_before") + ".png");
                    File.Copy(f, to, true);
                }
                Debug.Log($"[RealHeights] shots {(on ? "after" : "before")}: {CityElevation.RealReport}");
            }
            CityElevation.RprfOverride = null;
        }

        /// <summary>REAL HEIGHTS round 6 (the owner: "rectangles and diamonds of medians in the
        /// center of the highways"): the city solved once, then the PSX_SPOTS plans
        /// (MedianProfileProbe.Spots -> spot_probe.txt) and the PSX_PREVIEW_CUSTOM views
        /// (CityPreview, PSX_PREVIEW_SPOTS=custom -> Screenshots/City) on that solve.
        /// -executeMethod PSXRacing.EditorTools.CityRealHeights.RunMedianLook</summary>
        public static void RunMedianLook()
        {
            var map = CityMap.Get();
            if (map == null) return;
            CityElevation.Solve(map);
            if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PSX_SPOTS"))) MedianProfileProbe.Spots();
            CityPreview.Run();
            Debug.Log("[RealHeights] median look done");
        }

        /// <summary>REAL HEIGHTS junction surfaces + retaining walls: the
        /// PSX_PREVIEW_CUSTOM views on the measured solve, BEFORE (each fan
        /// from its arms' own heights, the buried-arm ease straight, a cut with
        /// no room for its 1V:3H back slope graded as a 1V:2H bank) and AFTER
        /// (the major road held through the fan, the smooth ease, the wall).
        /// -executeMethod PSXRacing.EditorTools.CityRealHeights.RunJunctionShots</summary>
        public static void RunJunctionShots()
        {
            string shots = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City");
            var names = new System.Collections.Generic.List<string>();
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_PREVIEW_CUSTOM") ?? "").Split(';'))
                if (item.Contains(":")) names.Add(item.Split(':')[0].Trim());
            var map = CityMap.Get();
            if (map == null) return;
            CityElevation.Solve(map);
            bool fm = CityMeshes.FanMajorOn, cb = CityMeshes.CutBankAllowed, js = CityMeshes.JoinStraight;
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    CityMeshes.FanMajorOn = on; CityMeshes.CutBankAllowed = !on; CityMeshes.JoinStraight = !on;
                    var t0 = System.DateTime.Now.AddSeconds(-2);
                    CityPreview.Run();
                    if (!Directory.Exists(shots)) continue;
                    foreach (var f in Directory.GetFiles(shots, "*.png"))
                    {
                        string b = Path.GetFileNameWithoutExtension(f);
                        if (b.EndsWith("_before") || b.EndsWith("_after") || File.GetLastWriteTime(f) < t0) continue;
                        bool mine = false; foreach (var n in names) if (b.Contains(n)) { mine = true; break; }
                        if (!mine) continue;
                        File.Copy(f, Path.Combine(shots, b + (on ? "_after" : "_before") + ".png"), true);
                    }
                    Debug.Log($"[RealHeights] junction shots {(on ? "after" : "before")}");
                }
            }
            finally { CityMeshes.FanMajorOn = fm; CityMeshes.CutBankAllowed = cb; CityMeshes.JoinStraight = js; }
        }

        /// <summary>REAL HEIGHTS round 3, the seats: the PSX_PREVIEW_CUSTOM
        /// views solved with the old seat zone (every metre beside the host,
        /// one station past it) - _before - and with the into-only zone
        /// (CityMeshes.SeatIntoOnly) - _after. Each pass re-solves the city.
        /// -executeMethod PSXRacing.EditorTools.CityRealHeights.RunSeatShots</summary>
        public static void RunSeatShots()
        {
            string shots = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City");
            var names = new System.Collections.Generic.List<string>();
            foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_PREVIEW_CUSTOM") ?? "").Split(';'))
                if (item.Contains(":")) names.Add(item.Split(':')[0].Trim());
            var map = CityMap.Get();
            if (map == null) return;
            bool keep = CityMeshes.SeatIntoEnv;
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    CityMeshes.SeatIntoEnv = on;
                    CityElevation.Solve(map);
                    var t0 = System.DateTime.Now.AddSeconds(-2);
                    CityPreview.Run();
                    if (!Directory.Exists(shots)) continue;
                    foreach (var f in Directory.GetFiles(shots, "*.png"))
                    {
                        string b = Path.GetFileNameWithoutExtension(f);
                        if (b.EndsWith("_before") || b.EndsWith("_after") || File.GetLastWriteTime(f) < t0) continue;
                        bool mine = false; foreach (var n in names) if (b.Contains(n)) { mine = true; break; }
                        if (!mine) continue;
                        File.Copy(f, Path.Combine(shots, b + (on ? "_after" : "_before") + ".png"), true);
                    }
                    Debug.Log($"[RealHeights] seat shots {(on ? "after" : "before")}");
                }
            }
            finally { CityMeshes.SeatIntoEnv = keep; }
        }

        static void Dump(CityMap map, string path)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("edge\ti\ts\ty\tdrawn\telev\n");
            foreach (var e in map.edges)
            {
                if (e.stS == null) continue;
                for (int i = 0; i < e.stS.Length; i++)
                    sb.Append(e.index).Append('\t').Append(i).Append('\t').Append(e.stS[i].ToString("0.00", inv)).Append('\t')
                      .Append(e.stY[i].ToString("0.000", inv)).Append('\t').Append(e.YAt(e.stS[i]).ToString("0.000", inv)).Append('\t')
                      .Append(e.stElev[i] ? '1' : '0').Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        static float ClassMaxGrade(CityMap.Edge e) =>
            e.link ? 0.08f : e.cls >= 5 ? 0.05f : e.cls >= 3 ? 0.07f : e.cls >= 1 ? 0.09f : 0.12f;

        static string Census(CityMap map, long ms)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"REAL HEIGHTS CENSUS (RPRF {(CityElevation.RprfOn ? "ON" : "OFF")}, REAL {(CityElevation.RealOn ? "ON" : "off")}): {map.edges.Length} edges, {map.measuredEdges} measured; solve {ms} ms ({CityElevation.LastSolvePhases})");
            sb.AppendLine("  " + CityElevation.RealReport);
            // grades past the class's limit, station to station (stations) and
            // on the drawn profile every 5 m, by tier, off structure and on
            var over = new int[4]; var overDrawn = new int[4]; var worst = new float[4]; var worstE = new int[4];
            for (int t = 0; t < 4; t++) worstE[t] = -1;
            foreach (var e in map.edges)
            {
                if (e.stS == null || e.stS.Length < 2 || e.length < 5f) continue;
                int t = CityTier.Of(e); float lim = ClassMaxGrade(e) + 1e-4f;
                for (int i = 1; i < e.stS.Length; i++)
                {
                    float g = Mathf.Abs(e.stY[i] - e.stY[i - 1]) / Mathf.Max(0.5f, e.stS[i] - e.stS[i - 1]);
                    if (g > lim) { over[t]++; if (g > worst[t]) { worst[t] = g; worstE[t] = e.index; } }
                }
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / 5f));
                for (int k = 1; k <= n; k++)
                {
                    float s0 = e.length * (k - 1) / n, s1 = e.length * k / n;
                    if (Mathf.Abs(e.YAt(s1) - e.YAt(s0)) / Mathf.Max(0.5f, s1 - s0) > lim) overDrawn[t]++;
                }
            }
            sb.AppendLine($"  GRADES past the class limit (motorway 5, trunk/primary 7, secondary/tertiary 9, local 12, ramps 8%): station steps T1 {over[1]} / T2 {over[2]} / T3 {over[3]} " +
                          $"(worst T1 {worst[1] * 100f:0.0}% e{worstE[1]}, T2 {worst[2] * 100f:0.0}% e{worstE[2]}, T3 {worst[3] * 100f:0.0}% e{worstE[3]}); drawn 5 m steps T1 {overDrawn[1]} / T2 {overDrawn[2]} / T3 {overDrawn[3]}");
            // clearance under every enforced crossing, on the drawn profile
            var on = CityElevation.EnforcedCrossings;
            int n2 = 0, fail = 0; float minC = 99f; int minCi = -1;
            var failList = new StringBuilder();
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                if (on != null && ci < on.Length && !on[ci]) continue;
                var c = map.crossings[ci];
                var O = map.edges[c.over]; var U = map.edges[c.under];
                CityElevation.ProjectOn(O, c.at, out float so);
                CityElevation.ProjectOn(U, c.at, out float su);
                float cl = O.YAt(so) - CityElevation.DeckThick - U.YAt(su);
                n2++;
                if (cl < minC) { minC = cl; minCi = ci; }
                if (cl < CityElevation.ClearanceM - 0.1f)
                {
                    fail++;
                    if (fail <= 20) failList.AppendLine(string.Format(inv, "    x{0} over e{1} '{2}' under e{3} '{4}' clear {5:0.00} m at ({6:0},{7:0})", ci, O.index, O.name, U.index, U.name, cl, c.at.x, c.at.y));
                }
            }
            sb.AppendLine($"  CLEARANCE under the enforced crossings (drawn): {n2} crossings, {fail} under {CityElevation.ClearanceM - 0.1f:0.0} m; lowest {minC:0.00} m (x{minCi})");
            sb.Append(failList);
            return sb.ToString();
        }
    }
}
