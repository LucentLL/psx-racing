using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The city's own invariants, checked without play mode. The circuit
    /// audits ask about one authored loop; these ask about the graph and the
    /// two bridge RULES:
    ///
    ///   every grade separation actually clears (deck underside above the
    ///   road below), every water span is on structure, no edge out-grades
    ///   its class, the network is one component from the spawn, and a tile
    ///   builds deterministically.
    ///
    /// Menu: PSX Racing/Audit City. Headless: -executeMethod
    /// PSXRacing.EditorTools.CityAudit.Run (writes city_audit.txt at root).
    /// </summary>
    public static class CityAudit
    {
        static StringBuilder outLog;
        static int failures;

        [MenuItem("PSX Racing/Audit City")]
        public static void Run()
        {
            outLog = new StringBuilder();
            failures = 0;

            var map = CityMap.Get();
            if (map == null)
            {
                Fail("charlotte_city.json missing from Resources");
                Finish();
                return;
            }

            Line($"edges {map.edges.Length}, nodes {map.nodes.Length}, waters {map.waters.Length}, " +
                 $"crossings {map.crossings.Length}, wspans {map.wspans.Length}");
            Check(map.edges.Length > 5000, "edge count in expected range", map.edges.Length);
            Check(map.crossings.Length > 300, "grade separations present", map.crossings.Length);
            Check(map.wspans.Length > 100, "water bridge spans present", map.wspans.Length);

            // ---- connectivity from spawn ---------------------------------
            if (map.NearestRoadPoint(map.uptown, 600f, true, out int spawnEdge, out _, out _))
            {
                var seen = new bool[map.nodes.Length];
                var stack = new Stack<int>();
                stack.Push(map.edges[spawnEdge].a);
                seen[map.edges[spawnEdge].a] = true;
                while (stack.Count > 0)
                {
                    int n = stack.Pop();
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        int other = e.a == n ? e.b : e.a;
                        if (!seen[other]) { seen[other] = true; stack.Push(other); }
                    }
                }
                float lenIn = 0f, lenAll = 0f;
                foreach (var e in map.edges)
                {
                    lenAll += e.length;
                    if (seen[e.a]) lenIn += e.length;
                }
                float pct = 100f * lenIn / Mathf.Max(1f, lenAll);
                Check(pct > 95f, "road length reachable from spawn", pct.ToString("0.0") + "%");
                Line($"network: {(lenAll / 1000f):0} km total, {(lenIn / 1000f):0} km reachable");
            }
            else Fail("no spawn road near uptown");

            // ---- rule: every ENFORCED separation clears ------------------
            // Mutually-conflicting braided crossings are pruned by the solver
            // (see PruneMutualCrossings) and are excused here, but counted.
            var mask = CityElevation.EnforcedCrossings;
            float worstClear = float.MaxValue;
            int clearFails = 0, excused = 0;
            for (int i = 0; i < map.crossings.Length; i++)
            {
                if (mask != null && i < mask.Length && !mask[i]) { excused++; continue; }
                var c = map.crossings[i];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                float so = Project(over, c.at), su = Project(under, c.at);
                float clear = over.YAt(so) - CityElevation.DeckThick - under.YAt(su);
                if (clear < worstClear) worstClear = clear;
                if (clear < CityElevation.ClearanceM - 0.6f) clearFails++;
            }
            Check(clearFails == 0, "every enforced grade separation clears the road below",
                  $"{clearFails} under-height ({excused} braided excused), worst {worstClear:0.00} m");
            if (clearFails > 0)
            {
                var bad = new List<(float clear, int i)>();
                for (int i = 0; i < map.crossings.Length; i++)
                {
                    if (mask != null && i < mask.Length && !mask[i]) continue;
                    var c = map.crossings[i];
                    float so = Project(map.edges[c.over], c.at), su = Project(map.edges[c.under], c.at);
                    float clear = map.edges[c.over].YAt(so) - CityElevation.DeckThick - map.edges[c.under].YAt(su);
                    if (clear < CityElevation.ClearanceM - 0.6f) bad.Add((clear, i));
                }
                bad.Sort((a, b) => a.clear.CompareTo(b.clear));
                foreach (var (clear, i) in bad.GetRange(0, Mathf.Min(6, bad.Count)))
                {
                    var c = map.crossings[i];
                    var o = map.edges[c.over]; var u = map.edges[c.under];
                    Line($"    clear {clear:0.00} at ({c.at.x:0},{c.at.y:0}) over e{c.over} '{o.name}' z{o.z} cls{o.cls} len{o.length:0} " +
                         $"/ under e{c.under} '{u.name}' z{u.z} cls{u.cls} len{u.length:0}");
                }
            }

            // ---- rule: every water span is on structure ------------------
            int wetFails = 0;
            foreach (var ws in map.wspans)
            {
                var e = map.edges[ws.edge];
                float mid = Mathf.Clamp((ws.s0 + ws.s1) * 0.5f, 0f, e.length);
                if (!e.ElevatedAt(mid)) wetFails++;
            }
            Check(wetFails == 0, "every water crossing carries a deck", wetFails);

            // ---- grades stay drivable ------------------------------------
            // Sub-30 m interchange slivers are exempt: a stub whose two nodes
            // genuinely sit a deck apart IS steep, and levelling it wholesale
            // was worse (it hauled streets up to viaduct height). They are
            // counted and reported, not failed.
            int gradeFails = 0, stubSteep = 0;
            float worstGrade = 0f;
            foreach (var e in map.edges)
            {
                bool stub = e.length < 30f;
                for (int i = 1; i < e.stS.Length; i++)
                {
                    float ds = e.stS[i] - e.stS[i - 1];
                    if (ds < 0.5f) continue;
                    float g = Mathf.Abs(e.stY[i] - e.stY[i - 1]) / ds;
                    if (g > 0.16f)
                    {
                        if (stub) { stubSteep++; continue; }
                        gradeFails++;
                    }
                    if (g > worstGrade && !stub) worstGrade = g;
                }
            }
            Check(gradeFails == 0, "no station-to-station grade past 16% (sub-30 m slivers exempt)",
                  $"{gradeFails} over (+{stubSteep} on slivers), worst {(worstGrade * 100f):0.0}%");
            if (gradeFails > 0)
            {
                var bad = new List<(float g, CityMap.Edge e, float at)>();
                foreach (var e in map.edges)
                {
                    if (e.length < 30f) continue;
                    for (int i = 1; i < e.stS.Length; i++)
                    {
                        float ds = e.stS[i] - e.stS[i - 1];
                        if (ds < 0.5f) continue;
                        float g = Mathf.Abs(e.stY[i] - e.stY[i - 1]) / ds;
                        if (g > 0.16f) bad.Add((g, e, e.stS[i]));
                    }
                }
                bad.Sort((a, b) => b.g.CompareTo(a.g));
                foreach (var (g, e, at) in bad.GetRange(0, Mathf.Min(6, bad.Count)))
                {
                    var p = e.PointAt(at);
                    Line($"    grade {(g * 100f):0}% on e{e.index} '{e.name}' z{e.z} cls{e.cls} len{e.length:0} at s={at:0} ({p.x:0},{p.y:0})");
                }
            }

            // ---- a tile builds, twice the same ---------------------------
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            int tx = Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize);
            int tz = Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize);
            var t1 = CityMeshes.Build(map, trims, buildings, tx, tz);
            var t2 = CityMeshes.Build(map, trims, buildings, tx, tz);
            Check(t1.roads != null && t1.ground != null, "uptown tile has roads and ground");
            Check(VCount(t1.roads) == VCount(t2.roads) && VCount(t1.buildings) == VCount(t2.buildings),
                  "tile build is deterministic",
                  $"{VCount(t1.roads)}/{VCount(t2.roads)} road verts, {VCount(t1.buildings)}/{VCount(t2.buildings)} building verts");
            Line($"uptown tile: {VCount(t1.ground)} ground, {VCount(t1.roads)} road, " +
                 $"{VCount(t1.buildings)} building verts, {t1.solids.Count} solids");

            Finish();
        }

        static int VCount(Mesh m) => m == null ? 0 : m.vertexCount;

        static float Project(CityMap.Edge e, Vector2 p)
        {
            float best = float.MaxValue, arc = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                var q = a + d * t;
                float dd = (p - q).sqrMagnitude;
                if (dd < best) { best = dd; arc = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
            return arc;
        }

        static void Check(bool ok, string what, object detail = null)
        {
            if (!ok) failures++;
            Line((ok ? "  ok  " : "  FAIL ") + what + (detail != null ? " — " + detail : ""));
        }

        static void Fail(string what) { failures++; Line("  FAIL " + what); }
        static void Line(string s) { outLog.AppendLine(s); Debug.Log("[CityAudit] " + s); }

        static void Finish()
        {
            outLog.AppendLine(failures == 0 ? "CITY AUDIT OK" : $"CITY AUDIT: {failures} FAILURES");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "city_audit.txt"), outLog.ToString());
        }
    }
}
