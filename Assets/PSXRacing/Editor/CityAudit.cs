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

            DriveAudit(map, trims, buildings);

            Finish();
        }

        /// <summary>A freeway-to-freeway interchange: the centroid of the
        /// crossings between two named roads, clustered by place (I-77 meets
        /// I-485 twice), with the mainline edge nearest the middle.</summary>
        public struct Interchange { public Vector2 at; public CityMap.Edge mainline; public float s; public int crossings; }

        public static List<Interchange> Interchanges(CityMap map, string nameA, string nameB)
        {
            bool Is(CityMap.Edge e, string n) => e.name == n || (n.Length > 4 && e.name.Contains(n));
            var clusters = new List<(Vector2 sum, int n, List<Vector2> pts)>();
            foreach (var c in map.crossings)
            {
                var o = map.edges[c.over]; var u = map.edges[c.under];
                if (!((Is(o, nameA) && Is(u, nameB)) || (Is(o, nameB) && Is(u, nameA)))) continue;
                int hit = -1;
                for (int i = 0; i < clusters.Count; i++)
                    if (Vector2.Distance(clusters[i].sum / clusters[i].n, c.at) < 1100f) { hit = i; break; }
                if (hit < 0) clusters.Add((c.at, 1, new List<Vector2> { c.at }));
                else { var cl = clusters[hit]; cl.pts.Add(c.at); clusters[hit] = (cl.sum + c.at, cl.n + 1, cl.pts); }
            }
            var result = new List<Interchange>();
            foreach (var cl in clusters)
            {
                var at = cl.sum / cl.n;
                CityMap.Edge best = null; float bd = float.MaxValue, bs = 0f;
                foreach (var e in map.edges)
                {
                    if (e.link || !Is(e, nameA)) continue;
                    if (Vector2.Distance(e.PointAt(e.length * 0.5f), at) > 1500f) continue;
                    CityElevation.ProjectOn(e, at, out float s);
                    float d = Vector2.Distance(e.PointAt(s), at);
                    if (d < bd) { bd = d; best = e; bs = s; }
                }
                if (best != null) result.Add(new Interchange { at = at, mainline = best, s = bs, crossings = cl.n });
            }
            return result;
        }

        /// <summary>
        /// THE DRIVE AUDIT. Everything above reasons about numbers; this one
        /// stands real tiles up with their colliders and asks the questions
        /// the car asks: is there a surface under every lane, is it where
        /// the solver said, does it step, and is anything solid standing
        /// across the lane at wheel height. Rays, not arithmetic — a second
        /// copy of the builder's sums agrees with the first while both are
        /// wrong. Runs on the tiles that have been wrong before: uptown, the
        /// West 5th Street bridge, the I-277/I-77 interchanges, a ramp merge
        /// and a piece of I-485.
        /// </summary>
        static void DriveAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            var spots = new List<(string name, Vector2 at)> { ("uptown", map.uptown) };
            foreach (var e in map.edges)
                if (e.bridge && e.name == "West 5th Street") { spots.Add(("w5th", e.PointAt(e.length * 0.5f))); break; }
            foreach (var ic in Interchanges(map, "I-277", "I-77")) spots.Add(("i277_i77", ic.at));
            foreach (var ic in Interchanges(map, "I-77", "I-485")) { spots.Add(("i77_i485", ic.at)); break; }
            foreach (var e in map.edges)
                if (e.link && e.cls >= 5 && Vector2.Distance(map.nodes[e.b], map.uptown) > 3000f) { spots.Add(("gore", map.nodes[e.b])); break; }
            foreach (var e in map.edges)
                if (e.name == "I-485" && !e.link && e.length > 300f) { spots.Add(("i485", e.PointAt(e.length * 0.5f))); break; }

            int walls = 0, steps = 0, holes = 0, off = 0, probes = 0;
            var notes = new List<(float sev, string what)>();
            void Note(float sev, string what) { notes.Add((sev, what)); }
            string Path(Collider c)
            {
                var t = c.transform; var sb = new StringBuilder(t.name);
                while (t.parent != null && !t.parent.name.StartsWith("~")) { t = t.parent; sb.Insert(0, t.name + "/"); }
                return sb.ToString();
            }
            // whose geometry is that: the nearest OTHER edge to a hit point
            string Owner(Vector3 at, int notEdge)
            {
                var p2 = new Vector2(at.x, at.z);
                var near = new HashSet<int>();
                map.EdgeSegsInRect(p2 - Vector2.one * 25f, p2 + Vector2.one * 25f, near);
                float bd = float.MaxValue; int bi = -1; float bs = 0f;
                foreach (var packed in near)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    if (oi == notEdge) continue;
                    var o = map.edges[oi];
                    Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                    float L2 = dq.sqrMagnitude;
                    float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - q0, dq) / L2) : 0f;
                    float dd = Vector2.Distance(p2, q0 + dq * tt);
                    if (dd < bd) { bd = dd; bi = oi; bs = o.s[si] + Mathf.Sqrt(L2) * tt; }
                }
                if (bi < 0) return " (no other edge near)";
                var oe = map.edges[bi];
                return $" nearest other: e{bi} '{oe.name}'{(oe.link ? " L" : "")}{(oe.bridge ? " B" : "")} cls{oe.cls} hw {trims.HalfWidthAt(oe, bs):0.0} at {bd:0.0} m, its y {oe.YAt(bs):0.00}, elev {oe.ElevatedAt(bs)}";
            }

            var sectionDumps = new List<string>();
            var root = new GameObject("~driveAudit");
            try
            {
                var segs = new HashSet<int>();
                foreach (var (spot, at) in spots)
                {
                    int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
                    int ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
                    var tiles = new List<GameObject>();
                    // the centre tile LAST, so CityMeshes' per-tile clip table
                    // describes the tile the probes run on
                    for (int k = 0; k < 9; k++)
                        {
                            int dx = k < 8 ? (k % 3) - 1 : 0, dz = k < 8 ? (k / 3) - 1 : 0;
                            if (k < 8 && dx == 0 && dz == 0) continue;
                            var tm = CityMeshes.Build(map, trims, buildings, ptx + dx, ptz + dz);
                            var go = new GameObject($"tile_{ptx + dx}_{ptz + dz}");
                            go.transform.SetParent(root.transform, false);
                            go.transform.position = tm.origin;
                            CityWorld.Attach(go, tm, null);
                            tiles.Add(go);
                        }
                    Physics.SyncTransforms();

                    var min = new Vector2(ptx * CityMeshes.TileSize, ptz * CityMeshes.TileSize);
                    var max = min + Vector2.one * CityMeshes.TileSize;
                    segs.Clear();
                    map.EdgeSegsInRect(min, max, segs);
                    var edges = new HashSet<int>();
                    foreach (var p in segs) edges.Add(p >> 12);
                    int wallsHere = 0, stepsHere = 0, holesHere = 0, offHere = 0;
                    foreach (var ei in edges)
                    {
                        var e = map.edges[ei];
                        float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
                        if (sMax - sMin < 1f) continue;
                        var prevY = new[] { float.NaN, float.NaN, float.NaN };
                        int stepN = 0;
                        for (float s = sMin; s <= sMax + 0.01f; s += 0.5f, stepN++)
                        {
                            var p = e.PointAt(s);
                            if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y)
                            { prevY[0] = prevY[1] = prevY[2] = float.NaN; continue; }
                            var tan = e.TangentAt(s);
                            var right = new Vector2(-tan.y, tan.x);
                            float y = e.YAt(s);
                            CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
                            float hw = Mathf.Min(hwL, hwR);
                            if (hwL + hwR < 1.2f) { prevY[0] = prevY[1] = prevY[2] = float.NaN; continue; }   // collapsed into its host
                            // three probes INSIDE the drawn ribbon: half a metre in
                            // from each edge and the middle of what is drawn (a
                            // clipped wedge's middle is not the centreline)
                            for (int k = 0; k < 3; k++)
                            {
                                float lat;
                                if (k == 0) { if (hwL < 1.1f) { prevY[k] = float.NaN; continue; } lat = -(hwL - 0.55f); }
                                else if (k == 2) { if (hwR < 1.1f) { prevY[k] = float.NaN; continue; } lat = hwR - 0.55f; }
                                else lat = (hwR - hwL) * 0.5f;
                                var w = new Vector3(p.x + right.x * lat, y + 3f, p.y + right.y * lat);
                                probes++;
                                if (!Physics.Raycast(w, Vector3.down, out var hit, 6.5f))
                                {
                                    holes++; holesHere++;
                                    Note(3f, $"HOLE  {spot} e{ei} '{e.name}'{(e.link ? " L" : "")} s={s:0} lane{k} at ({w.x:0},{w.z:0}) roadY {y:0.00}{CityMeshes.DescribeClip(map, trims, e, s)}");
                                    prevY[k] = float.NaN;
                                    continue;
                                }
                                float d = hit.point.y - y;
                                if (Mathf.Abs(d) > 0.35f)
                                {
                                    off++; offHere++;
                                    Note(Mathf.Abs(d), $"OFF   {spot} e{ei} '{e.name}'{(e.link ? " L" : "")} s={s:0} lane{k} surface {d:+0.00;-0.00} m from the solve, hit {Path(hit.collider)} at ({w.x:0},{w.z:0}){CityMeshes.DescribeClip(map, trims, e, s)}{Owner(hit.point, ei)}");
                                }
                                if (!float.IsNaN(prevY[k]) && Mathf.Abs(hit.point.y - prevY[k]) > 0.12f)
                                {
                                    steps++; stepsHere++;
                                    Note(Mathf.Abs(hit.point.y - prevY[k]) + 1f, $"STEP  {spot} e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} lane{k} {hit.point.y - prevY[k]:+0.00;-0.00} m over 0.5 m, on {Path(hit.collider)} at ({w.x:0},{w.z:0}) deg{map.nodeEdges[e.a].Count}/{map.nodeEdges[e.b].Count} trims {trims.atA[ei]:0.0}/{trims.atB[ei]:0.0}{CityMeshes.DescribeClip(map, trims, e, s)}{Owner(hit.point, ei)}");
                                }
                                prevY[k] = hit.point.y;
                            }
                            if (stepN % 4 != 0) continue;
                            // not on the end lines: a mitred rail end lies exactly there
                            if (s < sMin + 1.5f || s > sMax - 1.5f) continue;
                            // a deck's rail stands 0.3 m INSIDE the deck edge
                            if (hwL + hwR < 2.0f) continue;
                            var a = new Vector3(p.x - right.x * (hwL - 0.6f), y + 0.5f, p.y - right.y * (hwL - 0.6f));
                            var b = new Vector3(p.x + right.x * (hwR - 0.6f), y + 0.5f, p.y + right.y * (hwR - 0.6f));
                            var dir = b - a; float len = dir.magnitude;
                            if (len < 0.6f) continue;
                            dir /= len;
                            if (Physics.Raycast(a, dir, out var h1, len) || Physics.Raycast(b, -dir, out h1, len))
                            {
                                walls++; wallsHere++;
                                Note(2f, $"WALL  {spot} e{ei} '{e.name}'{(e.link ? " L" : "")} s={s:0}/{e.length:0} hw {hwL:0.0}/{hwR:0.0} across the lane: {Path(h1.collider)} at ({h1.point.x:0},{h1.point.z:0}) y {h1.point.y:0.00} (road {y:0.00}) lateral {Vector2.Dot(new Vector2(h1.point.x, h1.point.z) - p, right):+0.0;-0.0}{Owner(h1.point, ei)}");
                            }
                        }
                    }
                    Line($"drive {spot} at ({at.x:0},{at.y:0}): {edges.Count} edges, walls {wallsHere}, steps {stepsHere}, holes {holesHere}, off-surface {offHere}");
                    // the sections of the worst two edges on this tile, as drawn
                    var worst = new List<(float sev, int ei)>();
                    foreach (var (sev, what) in notes)
                    {
                        if (!what.Contains(" " + spot + " e")) continue;
                        int i0 = what.IndexOf(" e", what.IndexOf(spot)) + 2;
                        int i1 = what.IndexOf(' ', i0);
                        if (int.TryParse(what.Substring(i0, i1 - i0), out int wei) && !worst.Exists(w => w.ei == wei))
                            worst.Add((sev, wei));
                    }
                    worst.Sort((x, z) => z.sev.CompareTo(x.sev));
                    for (int wi = 0; wi < Mathf.Min(2, worst.Count); wi++)
                    {
                        var we = map.edges[worst[wi].ei];
                        sectionDumps.Add(CityMeshes.DescribeSections(map, trims, we));
                        // ...and the road it runs beside, if any
                        int hei = CityMeshes.HostEdgeAt(we, we.length * 0.5f);
                        if (hei >= 0) sectionDumps.Add(CityMeshes.DescribeSections(map, trims, map.edges[hei]));
                    }
                    foreach (var t in tiles) Object.DestroyImmediate(t);
                }
            }
            finally { Object.DestroyImmediate(root); }

            Line($"drive audit: {probes} probes on {spots.Count} tiles");
            Check(walls == 0, "nothing solid stands across any lane (drive audit)", walls);
            Check(steps == 0, "no lane surface steps more than 12 cm in half a metre (drive audit)", steps);
            Check(holes == 0, "every lane has a surface under it (drive audit)", holes);
            Check(off == 0, "every lane surface is where the solve put it (drive audit)", off);
            notes.Sort((p, q) => q.sev.CompareTo(p.sev));
            var seen = new HashSet<string>();
            int shown = 0;
            foreach (var (sev, what) in notes)
            {
                // one line per edge and kind, worst first
                string key = what.Substring(0, Mathf.Min(what.Length, what.IndexOf(" s=") > 0 ? what.IndexOf(" s=") : what.Length));
                if (!seen.Add(key)) continue;
                Line("    " + what);
                if (++shown >= 24) break;
            }
            foreach (var dump in sectionDumps) Line(dump.TrimEnd());
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
