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
    /// bridge RULES:
    ///
    ///   every grade separation actually clears (deck underside above the
    ///   road below), every OSM bridge is on structure end to end, every water
    ///   span is decked, no edge out-grades its class, the network is one
    ///   component from the spawn, the three race routes chain and close, a
    ///   tile builds deterministically, and no wall the emitter drew points
    ///   into its own building.
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
                Fail("charlotte_city.bytes missing from Resources");
                Finish();
                return;
            }

            Line($"edges {map.edges.Length}, nodes {map.nodes.Length}, waters {map.waters.Length}, " +
                 $"crossings {map.crossings.Length} ({CityElevation.TrenchCount} trenched), wspans {map.wspans.Length}, " +
                 $"footprints {map.footprints.Length}, routes {map.routes.Length}, DEM {(CityElevation.HasDem ? "yes" : "NO")}");
            Check(map.edges.Length > 20000, "edge count in expected range", map.edges.Length);
            Check(map.crossings.Length > 800, "grade separations present", map.crossings.Length);
            Check(map.wspans.Length > 200, "water bridge spans present", map.wspans.Length);
            Check(map.footprints.Length > 20000, "building footprints loaded", map.footprints.Length);
            Check(CityElevation.HasDem, "the SRTM height grid loaded");
            Check(CityElevation.TrenchCount > 20, "the inner freeways run in trenches under the streets", CityElevation.TrenchCount);
            Check(map.routes.Length == 3, "three race routes baked", map.routes.Length);

            int forced = 0;
            foreach (var c in map.crossings) if (c.forced) forced++;
            Check(forced > map.crossings.Length * 0.9f, "nearly every separation is decided by OSM's layer tags",
                  forced + " of " + map.crossings.Length);

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
            var mask = CityElevation.EnforcedCrossings;
            float worstClear = float.MaxValue;
            int clearFails = 0, excused = 0;
            var bad = new List<(float clear, int i)>();
            for (int i = 0; i < map.crossings.Length; i++)
            {
                if (mask != null && i < mask.Length && !mask[i]) { excused++; continue; }
                var c = map.crossings[i];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                CityElevation.ProjectOn(over, c.at, out float so);
                CityElevation.ProjectOn(under, c.at, out float su);
                float clear = over.YAt(so) - CityElevation.DeckThick - under.YAt(su);
                if (clear < worstClear) worstClear = clear;
                if (clear < CityElevation.ClearanceM - 0.6f) { clearFails++; bad.Add((clear, i)); }
            }
            Check(clearFails == 0, "every enforced grade separation clears the road below",
                  $"{clearFails} under-height ({excused} braided excused), worst {worstClear:0.00} m");
            if (clearFails > 0)
            {
                bad.Sort((a, b) => a.clear.CompareTo(b.clear));
                foreach (var (clear, i) in bad.GetRange(0, Mathf.Min(6, bad.Count)))
                {
                    var c = map.crossings[i];
                    var o = map.edges[c.over]; var u = map.edges[c.under];
                    Line($"    clear {clear:0.00} at ({c.at.x:0},{c.at.y:0}) over e{c.over} '{o.name}' L{o.layer} cls{o.cls} len{o.length:0} " +
                         $"/ under e{c.under} '{u.name}' L{u.layer} cls{u.cls} len{u.length:0}{(CityElevation.TrenchedCrossings[i] ? " TRENCH" : "")}");
                }
            }

            // ---- rule: every OSM bridge is a deck end to end ------------
            int bridgeEdges = 0, bridgeNotElev = 0;
            foreach (var e in map.edges)
            {
                if (!e.bridge) continue;
                bridgeEdges++;
                foreach (var el in e.stElev) if (!el) { bridgeNotElev++; break; }
            }
            Check(bridgeNotElev == 0, "every tagged bridge is on structure end to end",
                  bridgeNotElev + " of " + bridgeEdges + " have a grounded station");

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
            int gradeFails = 0, stubSteep = 0;
            float worstGrade = 0f;
            var badG = new List<(float g, CityMap.Edge e, float at)>();
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
                        badG.Add((g, e, e.stS[i]));
                    }
                    if (g > worstGrade && !stub) worstGrade = g;
                }
            }
            Check(gradeFails == 0, "no station-to-station grade past 16% (sub-30 m slivers exempt)",
                  $"{gradeFails} over (+{stubSteep} on slivers), worst {(worstGrade * 100f):0.0}%");
            if (gradeFails > 0)
            {
                badG.Sort((a, b) => b.g.CompareTo(a.g));
                foreach (var (g, e, at) in badG.GetRange(0, Mathf.Min(6, badG.Count)))
                {
                    var p = e.PointAt(at);
                    Line($"    grade {(g * 100f):0}% on e{e.index} '{e.name}' L{e.layer} cls{e.cls} len{e.length:0} at s={at:0} ({p.x:0},{p.y:0})" +
                         (e.bridge ? " BRIDGE" : "") + (e.link ? " LINK" : ""));
                    // The whole profile and both ends' company, so the next
                    // run explains itself instead of naming an edge.
                    var sb = new StringBuilder("      y:");
                    for (int i = 0; i < e.stY.Length; i++) sb.Append(' ').Append(e.stY[i].ToString("0.0")).Append(e.stElev[i] ? "^" : "");
                    Line(sb.ToString());
                    foreach (var n in new[] { e.a, e.b })
                    {
                        var nb = new StringBuilder($"      node {n} y={map.nodeY[n]:0.0} base={CityElevation.BaseY(map.nodes[n].x, map.nodes[n].y):0.0}:");
                        foreach (var oi in map.nodeEdges[n])
                        {
                            var o = map.edges[oi];
                            float endY = o.a == n ? o.stY[0] : o.stY[o.stY.Length - 1];
                            nb.Append($" e{oi}'{o.name}'{(o.link ? "L" : "")}{(o.bridge ? "B" : "")} len{o.length:0} end{endY:0.0}");
                        }
                        Line(nb.ToString());
                    }
                    for (int ci = 0; ci < map.crossings.Length; ci++)
                    {
                        var c = map.crossings[ci];
                        if (c.over != e.index && c.under != e.index) continue;
                        var o = map.edges[c.over == e.index ? c.under : c.over];
                        CityElevation.ProjectOn(e, c.at, out float sc);
                        Line($"      {(c.over == e.index ? "OVER" : "UNDER")} e{o.index} '{o.name}'{(o.link ? "L" : "")} at s={sc:0}{(CityElevation.TrenchedCrossings[ci] ? " TRENCH" : "")}{(mask != null && !mask[ci] ? " pruned" : "")}");
                    }
                }
            }

            // ---- the race routes chain through the graph -----------------
            foreach (var r in map.routes)
            {
                bool chained = true;
                float len = 0f;
                for (int k = 0; k < r.edges.Length; k++)
                {
                    var e = map.edges[r.edges[k]];
                    len += e.length;
                    if (k == 0) continue;
                    var p = map.edges[r.edges[k - 1]];
                    int pTo = r.dirs[k - 1] >= 0 ? p.b : p.a;
                    int from = r.dirs[k] >= 0 ? e.a : e.b;
                    if (pTo != from) { chained = false; break; }
                }
                Check(chained, "route " + r.id + " chains node to node", r.edges.Length + " edges");
                Check(Mathf.Abs(len - r.lengthM) < 2f, "route " + r.id + " length matches its edges",
                      len.ToString("0") + " vs " + r.lengthM.ToString("0"));
                if (r.loop)
                {
                    var first = map.edges[r.edges[0]]; var last = map.edges[r.edges[r.edges.Length - 1]];
                    int start = r.dirs[0] >= 0 ? first.a : first.b;
                    int end = r.dirs[r.dirs.Length - 1] >= 0 ? last.b : last.a;
                    Check(start == end, "route " + r.id + " closes into a ring");
                }
                var go = new GameObject("~routeProbe");
                var tp = go.AddComponent<TrackPath>();
                CityMode.BuildPath(map, r, tp);
                Check(tp.Count > 500 && Mathf.Abs(tp.Count * tp.spacing - r.lengthM) < TrackCatalog.Spacing * 2f,
                      "route " + r.id + " builds a path of the right length", tp.Count + " waypoints");
                if (r.loop)
                    Check(Vector3.Distance(tp.waypoints[tp.Count - 1], tp.waypoints[0]) <= tp.spacing * 1.5f,
                          "route " + r.id + " path closes into a ring",
                          Vector3.Distance(tp.waypoints[tp.Count - 1], tp.waypoints[0]).ToString("0.00") + " m");
                // The same waypoint-grade rule the self-test applies, with the
                // place named: a step between two stations is a node where two
                // edges' ends disagree, and the edge name is what finds it.
                float worstStep = 0f; int worstAt = -1;
                for (int i = 1; i < tp.Count; i++)
                {
                    float g = Mathf.Abs(tp.waypoints[i].y - tp.waypoints[i - 1].y) / tp.spacing;
                    if (g > worstStep) { worstStep = g; worstAt = i; }
                }
                string where = "";
                if (worstAt >= 0)
                {
                    var wp = tp.waypoints[worstAt];
                    map.NearestRoadPoint(new Vector2(wp.x, wp.z), 30f, false, out int wei, out _, out _);
                    where = $" at wp {worstAt} ({wp.x:0},{wp.z:0}) on e{wei} '{(wei >= 0 ? map.edges[wei].name : "?")}'";
                }
                Check(worstStep < 0.16f, "route " + r.id + " path grade stays drivable",
                      (worstStep * 100f).ToString("0.0") + "%" + where);
                if (worstStep >= 0.16f && worstAt >= 0)
                {
                    // The chain around the step: each edge's length, its two
                    // end stations and its nodes' heights. A step that is not
                    // at a node is inside an edge; one at a node is two ends
                    // that never met.
                    var wpA = tp.waypoints[worstAt - 1]; var wpB = tp.waypoints[worstAt];
                    Line($"      wp {worstAt - 1} y={wpA.y:0.00}  wp {worstAt} y={wpB.y:0.00}");
                    for (int k = 0; k < r.edges.Length; k++)
                    {
                        var e = map.edges[r.edges[k]];
                        float dA = Mathf.Min(Vector2.Distance(e.pts[0], new Vector2(wpB.x, wpB.z)), Vector2.Distance(e.pts[e.pts.Length - 1], new Vector2(wpB.x, wpB.z)));
                        if (dA > 60f) continue;
                        Line($"      chain[{k}] e{e.index} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} dir{r.dirs[k]} len{e.length:0} " +
                             $"y0={e.stY[0]:0.00} y1={e.stY[e.stY.Length - 1]:0.00} node{e.a}={map.nodeY[e.a]:0.00} node{e.b}={map.nodeY[e.b]:0.00} deg{map.nodeEdges[e.a].Count}/{map.nodeEdges[e.b].Count}");
                    }
                }
                Object.DestroyImmediate(go);
            }

            // ---- tiles build, twice the same, walls outward --------------
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
                 $"{VCount(t1.buildings)} building verts, {t1.solids.Count} solids, {t1.footprintCount} footprints, {t1.goreCount} gores");
            Check(t1.footprintCount > 8, "uptown tile is built from real footprints", t1.footprintCount);
            Check(t1.wallFacingErrors == 0, "every wall the emitter drew faces outward (uptown)", t1.wallFacingErrors);

            // A freeway tile: barriers, and a merge gore somewhere near.
            CityMap.Edge fwy = null;
            foreach (var e in map.edges) if (e.cls >= 5 && !e.link && e.name == "I-485" && e.length > 200f) { fwy = e; break; }
            if (fwy != null)
            {
                var p = fwy.PointAt(fwy.length * 0.5f);
                var tf = CityMeshes.Build(map, trims, buildings,
                    Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                Check(tf.barriers != null && tf.barriers.vertexCount > 0, "a freeway tile carries barriers",
                      tf.barriers != null ? tf.barriers.vertexCount : 0);
                Check(tf.wallFacingErrors == 0, "every wall faces outward (freeway tile)", tf.wallFacingErrors);
            }
            int gores = 0, tilesTried = 0;
            foreach (var e in map.edges)
            {
                if (!e.link || e.cls < 5) continue;
                var p = map.nodes[e.b];
                var tg = CityMeshes.Build(map, trims, buildings,
                    Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                gores += tg.goreCount;
                if (++tilesTried >= 6 || gores > 0) break;
            }
            Check(gores > 0, "ramp merges draw a gore", gores + " in " + tilesTried + " tiles");

            // The suburbs: houses between the arterials.
            int houses = 0;
            foreach (var e in map.edges)
            {
                if (e.cls != 2 || e.link) continue;
                var p = e.PointAt(e.length * 0.5f);
                if (Vector2.Distance(p, map.uptown) < 9000f || Vector2.Distance(p, map.uptown) > 12000f) continue;
                var ts = CityMeshes.Build(map, trims, buildings,
                    Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                houses = ts.houseCount;
                Check(ts.wallFacingErrors == 0, "every wall faces outward (suburb tile)", ts.wallFacingErrors);
                break;
            }
            Check(houses > 5, "a suburb tile fills its blocks with houses", houses);

            Finish();
        }

        static int VCount(Mesh m) => m == null ? 0 : m.vertexCount;

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
