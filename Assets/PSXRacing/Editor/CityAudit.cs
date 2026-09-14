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
            // 928 with the express lanes in the graph, 783 without them
            // (they crossed under and over a hundred ramps of their own)
            Check(map.crossings.Length > 700, "grade separations present", map.crossings.Length);
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

            GradeAudit(map);

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

                // THE PATH IS ON THE ROAD. Its heights come from the route's
                // own edges, and a height lerped between two OSM vertices
                // 200 m apart is not the road's between them — the grid stood
                // 1.5 m in the air over the 277 before BuildPath sampled the
                // stations too. Every waypoint against the solved surface of
                // the route edge it lies on.
                var routeEdges = new HashSet<int>(r.edges);
                float worstFloat = 0f; int floatAt = -1, floating = 0, judged = 0;
                for (int i = 0; i < tp.Count; i++)
                {
                    var wp = tp.waypoints[i];
                    if (!map.NearestRoadPoint(new Vector2(wp.x, wp.z), 12f, false, out int wei, out float wat, out _)) continue;
                    if (!routeEdges.Contains(wei)) continue;
                    judged++;
                    float dy = Mathf.Abs(wp.y - map.edges[wei].YAt(wat));
                    if (dy > 0.15f) floating++;
                    if (dy > worstFloat) { worstFloat = dy; floatAt = i; }
                }
                string fwhere = floatAt >= 0 ? $" at wp {floatAt} ({tp.waypoints[floatAt].x:0},{tp.waypoints[floatAt].z:0})" : "";
                Check(floating == 0 && judged > tp.Count / 2, "route " + r.id + " path sits on the solved road",
                      $"{floating} of {judged} waypoints off by more than 15 cm, worst {worstFloat:0.00} m{fwhere}");
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

            // ---- no road stands up out of another road's lanes ----------
            var census = new OverlapStats();
            OverlapCensus(map, trims, false, null, census);
            Check(census.branchPastAttach <= 60f,
                  "no ramp stands off its mainline inside the mainline's pavement (overlap census)",
                  $"{census.branchPastAttach:0} m of branch more than {BranchAttachDy} m off its host " +
                  $"(over 3100 m before ramps were seated); {census.metres:0} m of all ribbons at a wrong height, " +
                  $"{CityElevation.SeatedStationCount} stations seated");

            fanMouths = new FanMouthTally();
            DriveAudit(map, trims, buildings);
            RoadsideAudit(map, trims, buildings);
            ReportFanMouths();
            fanMouths = null;

            Finish();
        }

        /// <summary>What the overlap census measured, for a check.</summary>
        public class OverlapStats { public float metres, branchMetres, branchPastAttach; }
        /// <summary>CityMeshes' AttachDy: past this a tile stops clipping a
        /// branch against its host, and the branch is drawn whole inside it.</summary>
        const float BranchAttachDy = 0.6f;

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
        /// <summary>A creek bridge with no traced water under it: the DEM
        /// calls the land level there and the deck must still stand over a
        /// dip. A street-class tagged bridge of a few dozen metres within
        /// the core, off every water span.</summary>
        public static bool FindCreekBridge(CityMap map, out CityMap.Edge found)
        {
            found = null;
            var wet = new HashSet<int>();
            foreach (var ws in map.wspans) wet.Add(ws.edge);
            foreach (var e in map.edges)
            {
                if (!e.bridge || e.link || e.cls > 3 || e.length < 40f || e.length > 160f) continue;
                if (wet.Contains(e.index)) continue;
                if (Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown) > 6000f) continue;
                found = e; return true;
            }
            return false;
        }

        /// <summary>I-77 north of uptown, where the express lanes ran beside
        /// the general lanes until the 2026-09-12 export dropped them.</summary>
        public static bool FindI77North(CityMap map, out CityMap.Edge found)
        {
            found = null;
            foreach (var e in map.edges)
            {
                if (e.name != "I-77" || e.link || e.length < 250f) continue;
                var m = e.PointAt(e.length * 0.5f);
                if (m.y - map.uptown.y < 3000f || m.y - map.uptown.y > 9000f) continue;
                found = e; return true;
            }
            return false;
        }

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
            if (FindCreekBridge(map, out var creek)) spots.Add(("creek", creek.PointAt(creek.length * 0.5f)));
            if (FindI77North(map, out var i77n)) spots.Add(("i77n", i77n.PointAt(i77n.length * 0.5f)));

            int walls = 0, steps = 0, holes = 0, off = 0, grass = 0, probes = 0;
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
                    FanMouths(map, trims, ptx, ptz);

                    var min = new Vector2(ptx * CityMeshes.TileSize, ptz * CityMeshes.TileSize);
                    var max = min + Vector2.one * CityMeshes.TileSize;
                    segs.Clear();
                    map.EdgeSegsInRect(min, max, segs);
                    var edges = new HashSet<int>();
                    foreach (var p in segs) edges.Add(p >> 12);
                    int wallsHere = 0, stepsHere = 0, holesHere = 0, offHere = 0, grassHere = 0;
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
                                // THE LAND OVER THE TARMAC. The first thing
                                // a wheel meets in a lane must be the road:
                                // ground (or water) on top is grass through
                                // the pavement, however few centimetres of
                                // it — the OFF test below only sees it past
                                // 35 cm, and the owner sees it at two.
                                string hp = Path(hit.collider);
                                if (hp.EndsWith("/Ground") || hp.EndsWith("/Water"))
                                {
                                    grass++; grassHere++;
                                    Note(1.5f + Mathf.Abs(d), $"GRASS {spot} e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} s={s:0}/{e.length:0} lane{k} land {d:+0.00;-0.00} m from the solve and ON TOP of the tarmac: {hp} at ({w.x:0},{w.z:0}){(e.ElevatedAt(s) ? " deck" : "")} sag {e.SagAt(s):0.00}{Owner(hit.point, ei)}");
                                }
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
                    Line($"drive {spot} at ({at.x:0},{at.y:0}): {edges.Count} edges, walls {wallsHere}, steps {stepsHere}, holes {holesHere}, off-surface {offHere}, grass {grassHere}");
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
            Check(grass == 0, "no land stands on top of any lane (drive audit)", grass);
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

        // ==================================================================
        /// <summary>
        /// THE ROADSIDE AUDIT (2026-09-13). The drive audit fires every ray
        /// INSIDE the lanes, so it never saw what a car meets past the edge:
        /// a 20 cm kerb collider along every street, a pit beside every
        /// structure approach, open deck edges at wedges, noses and fans.
        /// This stands up the tiles the three race routes cross and the most
        /// elevated tiles in the city, and asks the car's questions from
        /// OUTSIDE the edge (RoadsideRules is the one table of thresholds):
        ///
        ///   VERGE  on a grounded edge that no rail, wall or other road
        ///          claims: the surface 5 cm out is within EdgeDropFailM of
        ///          the tarmac, and a ray at the body box's lowest clearance
        ///          over the ground a metre out, fired back at the edge, meets
        ///          no face steeper than a landing (normal.y 0.7).
        ///   RAILS  every metre of every ribbon side, fan chord and gore nose:
        ///          where the surface 1.5 m out is more than OpenDropM down, a
        ///          Solid-layer collider must stand across the edge — and across
        ///          any road surface carrying on flush beyond it — at wheel to
        ///          hip height (an overlap box, so a ray starting on a rail's
        ///          face cannot miss it the way CityEdgeProbe's did).
        ///   LANES  counted, not failed: land the first thing over a lane, or
        ///          anything solid within a car's height of it, every run
        ///          listed with what it is and whose edge or fan chord.
        ///   FANS   every lane mouth at a junction fan has road under it
        ///          (with the drive audit's tiles: <see cref="FanMouths"/>).
        ///   PITS   every lattice corner within PitReachM of a grounded
        ///          ribbon more than half a metre under that ribbon's own
        ///          design, labelled by the rule that put it there.
        /// </summary>
        const int RoadsideTopElevatedTiles = 12;
        /// <summary>Tiles kept standing at once while the probe walks the
        /// routes (a route's tiles are contiguous, so neighbours are reused).</summary>
        const int RoadsideLiveTiles = 40;

        static long TileKey(int tx, int tz) => ((long)tx << 32) | (uint)tz;

        /// <summary>The tiles the roadside audit probes: every tile a race
        /// route's edges cross, then the most elevated tiles in the city.</summary>
        public static List<(int tx, int tz, string why)> RoadsideTiles(CityMap map, int topElevated)
        {
            var tiles = new List<(int tx, int tz, string why)>();
            var seen = new HashSet<long>();
            void Add(Vector2 p, string why)
            {
                int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
                if (seen.Add(TileKey(tx, tz))) tiles.Add((tx, tz, why));
            }
            foreach (var r in map.routes)
                foreach (var ei in r.edges)
                {
                    var e = map.edges[ei];
                    for (float s = 0f; s < e.length; s += 16f) Add(e.PointAt(s), "route " + r.id);
                    Add(e.PointAt(e.length), "route " + r.id);
                }
            var elevM = new Dictionary<long, (float m, Vector2 at)>();
            foreach (var e in map.edges)
                for (int i = 0; i + 1 < e.stS.Length; i++)
                {
                    if (!e.stElev[i] && !e.stElev[i + 1]) continue;
                    var p = e.PointAt((e.stS[i] + e.stS[i + 1]) * 0.5f);
                    long k = TileKey(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    elevM.TryGetValue(k, out var v);
                    elevM[k] = (v.m + e.stS[i + 1] - e.stS[i], p);
                }
            var ranked = new List<(float m, Vector2 at)>(elevM.Values);
            ranked.Sort((a, b) => b.m.CompareTo(a.m));
            for (int i = 0; i < Mathf.Min(topElevated, ranked.Count); i++) Add(ranked[i].at, $"elevated {ranked[i].m:0} m");
            return tiles;
        }

        static void RoadsideAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            var probeTiles = RoadsideTiles(map, RoadsideTopElevatedTiles);
            var root = new GameObject("~roadsideAudit");
            var live = new Dictionary<long, (GameObject go, int used)>();
            int clock = 0;
            int solidMask = 1 << CityWorld.SolidLayer;

            int vergePoints = 0, vergeStepFails = 0, faceFails = 0, railPoints = 0, gapRuns = 0, nosesBuilt = 0;
            float gapMetres = 0f, dropMetres = 0f, vergeBuilt = 0f, railBuilt = 0f;
            var gapByKind = new Dictionary<string, float>();
            var pitByCause = new Dictionary<string, int>();
            int pits = 0, pitsUnexplained = 0;
            var notes = new List<(float sev, string what)>();
            // Each failing kind keeps its own list: sorted together, forty
            // OPEN and PIT lines crowded out every FACE line the audit found.
            var lipNotes = new List<(float sev, string what)>();
            var faceNotes = new List<(float sev, string what)>();
            var ledgeNotes = new List<(float sev, string what)>();
            int ledges = 0;
            int lanePoints = 0, laneLand = 0, laneSolid = 0, laneBand = 0;
            var laneRuns = new List<LaneRun>();
            var bandByClass = new SortedDictionary<string, int>();
            var bandCols = new Collider[8];
            string HitPath(RaycastHit h) => (h.collider.transform.parent != null ? h.collider.transform.parent.name + "/" : "") + h.collider.name;
            string NodeNote(CityMap.Edge e, float s) =>
                $"node {Mathf.Min(s, e.length - s):0} m deg{map.nodeEdges[e.a].Count}/{map.nodeEdges[e.b].Count} trims {trims.atA[e.index]:0.0}/{trims.atB[e.index]:0.0}";

            void DropTile(long k)
            {
                if (!live.TryGetValue(k, out var t)) return;
                foreach (var mf in t.go.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(mf.sharedMesh);
                Object.DestroyImmediate(t.go);
                live.Remove(k);
            }
            void StandTm(int tx, int tz, CityMeshes.TileMeshes tm)
            {
                var go = new GameObject($"tile_{tx}_{tz}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = tm.origin;
                CityWorld.Attach(go, tm, null);
                live[TileKey(tx, tz)] = (go, ++clock);
            }
            void Discard(CityMeshes.TileMeshes tm)
            {
                foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.buildings })
                    if (m != null) Object.DestroyImmediate(m);
            }

            // the highest surface under a point, any layer, triggers ignored
            bool Highest(Vector3 from, float reach, out RaycastHit best)
            {
                best = default; bool found = false;
                foreach (var h in Physics.RaycastAll(from, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
                    if (!found || h.point.y > best.point.y) { best = h; found = true; }
                return found;
            }
            bool Claimed(RaycastHit h) => h.collider.gameObject.layer == CityWorld.SolidLayer || h.collider.name == "Roads";

            // One probe point on a drawn edge: is it an unguarded drop?
            // Returns 0 = no drop, 1 = guarded drop, 2 = OPEN drop.
            int RailProbe(Vector3 edge, Vector2 outw, Vector2 along)
            {
                railPoints++;
                var o3 = new Vector3(outw.x, 0f, outw.y);
                float drop = 80f;
                if (Highest(edge + o3 * 1.5f + Vector3.up * 1.0f, 81f, out var h))
                {
                    if (h.collider.gameObject.layer == CityWorld.SolidLayer) return 1;
                    drop = edge.y - h.point.y;
                }
                if (drop <= RoadsideRules.OpenDropM) return 0;
                // WHERE THE CAR WOULD FALL FROM. Past a host's edge in its gore
                // gap the surface carries on as the branch's own pavement, flush,
                // for as far as the branch is wide, and the branch's outer rail
                // stands at ITS edge: the fall is past that rail. A box that only
                // spanned the host's edge called 20 of those seams open deck (a
                // ramp sliver 0.6-1.5 m wide has its rail beyond the box and the
                // drop beyond the 1.5 m probe). So walk out over road surface
                // within a decimetre of this edge's height first, and look for a
                // barrier across the whole walk. A real hole — no surface, or a
                // step — stops the walk at the edge, as before.
                float reach = CityMeshes.RailOverhangM;
                for (float d = 0.1f; d < 1.45f; d += 0.1f)
                {
                    if (!Highest(edge + o3 * d + Vector3.up * 0.3f, 0.6f, out var hs) || hs.collider.name != "Roads" ||
                        Mathf.Abs(hs.point.y - edge.y) > 0.1f) break;
                    reach = d + CityMeshes.RailOverhangM;
                }
                // across the walk from RailW inside the edge to RailOverhangM
                // past its end, from the lower barrier ray height to the upper
                float lo = RoadsideRules.BarrierRayHeights[0], hi = RoadsideRules.BarrierRayHeights[RoadsideRules.BarrierRayHeights.Length - 1];
                var centre = edge + o3 * ((reach - 0.6f) * 0.5f) + Vector3.up * ((lo + hi) * 0.5f);
                var half = new Vector3((reach + 0.6f) * 0.5f, (hi - lo) * 0.5f, 0.45f);
                var rot = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y), Vector3.up);
                return Physics.CheckBox(centre, half, rot, solidMask, QueryTriggerInteraction.Ignore) ? 1 : 2;
            }

            try
            {
                var segs = new HashSet<int>();
                var chords = new List<(Vector3 a, Vector3 b, Vector2 outward)>();
                foreach (var (tx, tz, why) in probeTiles)
                {
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            long nk = TileKey(tx + dx, tz + dz);
                            if (live.TryGetValue(nk, out var t)) { live[nk] = (t.go, ++clock); continue; }
                            StandTm(tx + dx, tz + dz, CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz));
                        }
                    // the centre LAST: CityMeshes' clip and gore tables are then this tile's
                    var tmC = CityMeshes.Build(map, trims, buildings, tx, tz);
                    vergeBuilt += tmC.vergeMetres; railBuilt += tmC.railMetres; nosesBuilt += tmC.goreNoses.Count;
                    long ck = TileKey(tx, tz);
                    if (live.TryGetValue(ck, out var ct)) { live[ck] = (ct.go, ++clock); Discard(tmC); }
                    else StandTm(tx, tz, tmC);
                    while (live.Count > RoadsideLiveTiles)
                    {
                        long oldest = 0; int ou = int.MaxValue;
                        foreach (var kv in live)
                        {
                            long kx = kv.Key >> 32, kz = (int)(kv.Key & 0xFFFFFFFF);
                            if (Mathf.Abs(kx - tx) <= 1 && Mathf.Abs(kz - tz) <= 1) continue;
                            if (kv.Value.used < ou) { ou = kv.Value.used; oldest = kv.Key; }
                        }
                        if (ou == int.MaxValue) break;
                        DropTile(oldest);
                    }
                    Physics.SyncTransforms();
                    FanMouths(map, trims, tx, tz);

                    var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
                    var max = min + Vector2.one * CityMeshes.TileSize;
                    bool In(Vector2 p) => p.x >= min.x && p.x < max.x && p.y >= min.y && p.y < max.y;
                    segs.Clear();
                    map.EdgeSegsInRect(min, max, segs);
                    var edges = new HashSet<int>();
                    foreach (var p in segs) edges.Add(p >> 12);

                    // ---- ribbon sides ----------------------------------------
                    foreach (var ei in edges)
                    {
                        var e = map.edges[ei];
                        float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
                        if (sMax - sMin < 2f) continue;
                        var ends = CityMeshes.StructureEndsOf(map, trims, e);
                        float[] run = { 0f, 0f };
                        string[] runKind = { "", "" };
                        Vector3[] runAt = { default, default };
                        void CloseRun(int si)
                        {
                            if (run[si] >= RoadsideRules.DeckRailGapFailM)
                            {
                                gapRuns++; gapMetres += run[si];
                                gapByKind.TryGetValue(runKind[si], out float gm); gapByKind[runKind[si]] = gm + run[si];
                                CityElevation.ProjectOn(e, new Vector2(runAt[si].x, runAt[si].z), out float sAt);
                                notes.Add((run[si], $"OPEN  {run[si]:0} m of {runKind[si]} edge over a drop, e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} side {(si == 0 ? "L" : "R")} ending at s={sAt:0} ({runAt[si].x:0},{runAt[si].z:0}) tile {tx},{tz}{CityMeshes.DescribeClip(map, trims, e, sAt)}{CityMeshes.DescribeSide(map, trims, e, sAt, si == 0 ? -1 : 1)}"));
                            }
                            run[si] = 0f;
                        }
                        for (float s = sMin + 0.5f; s <= sMax - 0.5f; s += 1f)
                        {
                            var p = e.PointAt(s);
                            if (!In(p)) { CloseRun(0); CloseRun(1); continue; }
                            var tan = e.TangentAt(s);
                            var right = new Vector2(-tan.y, tan.x);
                            float y = e.YAt(s);
                            CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
                            bool elev = e.ElevatedAt(s);
                            bool approach = false;
                            foreach (var se in ends) if (Mathf.Abs(s - se) < CityMeshes.ApproachRailM) { approach = true; break; }
                            string kind = elev ? "deck" : approach ? "approach" : "grounded ledge";
                            bool probeVerge = !elev && (int)(s - sMin) % 2 == 0;   // every other metre
                            // THE LANES, EVERYWHERE THE ROADSIDE IS PROBED. The
                            // drive audit asks its lane questions on nine tiles;
                            // a verge, seam or rail laid over the next road's
                            // lanes can be anywhere two ribbons are drawn into
                            // each other. Decks too, every other metre: a rail
                            // standing in a lane is worst on a bridge, and the
                            // verge probe's "grounded only" had no reason to
                            // apply here.
                            //
                            // Two questions per probe (2026-09-14). The first
                            // thing a wheel meets from above: land is grass
                            // through the tarmac. And whether anything SOLID
                            // stands within a car's height of the lane surface
                            // (RoadsideRules.CarBandM), asked with a slim column
                            // overlap: the old "first hit from three metres up
                            // is a barrier" counted an upper deck's rail tops a
                            // car passes under, and missed a rail whose face
                            // stood beside the probe with its top out of line.
                            // Every hit is listed with what it is and whose.
                            if ((int)(s - sMin) % 2 == 0 && hwL + hwR >= 1.2f)
                                for (int k = 0; k < 3; k++)
                                {
                                    float lat;
                                    if (k == 0) { if (hwL < 1.1f) continue; lat = -(hwL - 0.55f); }
                                    else if (k == 2) { if (hwR < 1.1f) continue; lat = hwR - 0.55f; }
                                    else lat = (hwR - hwL) * 0.5f;
                                    var w = new Vector3(p.x + right.x * lat, y + 3f, p.y + right.y * lat);
                                    lanePoints++;
                                    if (Physics.Raycast(w, Vector3.down, out var lh, 6.5f, ~0, QueryTriggerInteraction.Ignore))
                                    {
                                        float dl = lh.point.y - y;
                                        if (lh.collider.name == "Ground")
                                        {
                                            laneLand++;
                                            string owner = LaneOwner(map, trims, ei, new Vector2(lh.point.x, lh.point.z), y, out string cls);
                                            AddLaneRun(laneRuns, "land", e, k, s, tx, tz, $"land {dl:+0.00;-0.00} m on {HitPath(lh)}", cls, owner);
                                        }
                                        else if (lh.collider.gameObject.layer == CityWorld.SolidLayer && dl > 0.35f) laneSolid++;
                                    }
                                    var bandC = new Vector3(w.x, y + (LaneBandFloorM + RoadsideRules.CarBandM) * 0.5f, w.z);
                                    var bandH = new Vector3(LaneColumnM, (RoadsideRules.CarBandM - LaneBandFloorM) * 0.5f, LaneColumnM);
                                    int nCols = Physics.OverlapBoxNonAlloc(bandC, bandH, bandCols, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore);
                                    if (nCols == 0) continue;
                                    laneBand++;
                                    var col = bandCols[0];
                                    string colPath = (col.transform.parent != null ? col.transform.parent.name + "/" : "") + col.name;
                                    // how high it stands over the lane: the solid surfaces a
                                    // vertical line through the column crosses in the band
                                    float top = float.NaN, low = float.NaN;
                                    Vector3 at3 = new Vector3(w.x, y + 1f, w.z);
                                    bool back = Physics.queriesHitBackfaces;
                                    Physics.queriesHitBackfaces = true;
                                    try
                                    {
                                        foreach (var hb in Physics.RaycastAll(new Vector3(w.x, y + RoadsideRules.CarBandM, w.z), Vector3.down,
                                                                              RoadsideRules.CarBandM - LaneBandFloorM, solidMask, QueryTriggerInteraction.Ignore))
                                        {
                                            if (float.IsNaN(top) || hb.point.y > top) { top = hb.point.y; at3 = hb.point; }
                                            if (float.IsNaN(low) || hb.point.y < low) low = hb.point.y;
                                        }
                                    }
                                    finally { Physics.queriesHitBackfaces = back; }
                                    string height = float.IsNaN(top) ? "a face beside the lane line" : Mathf.Abs(top - low) < 0.02f ? $"{top - y:+0.00;-0.00} m" : $"{low - y:+0.00;-0.00}..{top - y:+0.00;-0.00} m";
                                    string cls2 = "a building", owner2 = "";
                                    if (col.name == "Solid") cls2 = "a pier or solid box";
                                    else if (col.name != "Buildings") owner2 = LaneOwner(map, trims, ei, new Vector2(at3.x, at3.z), y, out cls2);
                                    bandByClass.TryGetValue(cls2, out int bc); bandByClass[cls2] = bc + 1;
                                    AddLaneRun(laneRuns, "band", e, k, s, tx, tz, $"{colPath} {height}", cls2, owner2);
                                }
                            for (int si = 0; si < 2; si++)
                            {
                                int side = si == 0 ? -1 : 1;
                                float hw = side < 0 ? hwL : hwR;
                                if (hw < 0.3f) { CloseRun(si); continue; }   // clipped to nothing: the host's edge is the edge
                                var outw = right * side;
                                var edgeW = new Vector3(p.x + outw.x * hw, y, p.y + outw.y * hw);

                                int r = RailProbe(edgeW, outw, tan);
                                if (r == 2) { dropMetres += 1f; run[si] += 1f; runKind[si] = kind; runAt[si] = edgeW; }
                                else CloseRun(si);
                                if (r != 0) continue;

                                if (!probeVerge) continue;
                                var o3 = new Vector3(outw.x, 0f, outw.y);
                                if (!Highest(edgeW + o3 * 0.05f + Vector3.up * 1.2f, 4f, out var h0) || Claimed(h0)) continue;
                                vergePoints++;
                                // THE STEP FROM THE TARMAC A WHEEL LEAVES. RoadsideRules'
                                // edge drop is tarmac surface to shoulder surface, and
                                // that is measured from the pavement as DRAWN just
                                // inside the edge: the ribbon is flat between its
                                // cross-sections and mitred along a bend's bisector, so
                                // on a grade its edge stands a few centimetres off the
                                // solved height (a 5.4 cm lip read off a verge 2.5 cm
                                // under the tarmac beside e9796). Where the tarmac as
                                // drawn ends short of the analytic edge — a chord across
                                // the outside of a bend, a squeeze between two sections —
                                // the point 5 cm past that edge is out on the verge's
                                // slope, so the step is measured 5-10 cm past where the
                                // tarmac really ends instead, found by walking back in
                                // over the verge, as RailProbe walks out over flush
                                // pavement to find where a car would fall from. The
                                // first point still counts: it is ground a wheel rolls
                                // onto that far past the tarmac, and no recoverable
                                // roadside has fallen more than 1V:4H on the way. Read
                                // 5-10 cm past only, a squeeze strip a hand wide hid the
                                // slot 22 cm deep beyond it (North Tryon Street's two
                                // carriageways, e10901 beside e10819).
                                float yTar = y;
                                bool onTarmac = Highest(edgeW - o3 * 0.05f + Vector3.up * 0.3f, 0.6f, out var hr) && hr.collider.name == "Roads";
                                if (onTarmac) yTar = hr.point.y;
                                float step = yTar - h0.point.y;
                                string measured = "5 cm past the edge";
                                if (step > RoadsideRules.EdgeDropFailM && !onTarmac)
                                {
                                    // as far as the notch a bisector corner leaves on the
                                    // outside of a right-angle bend (1.1 m on Arlington
                                    // Avenue's, a street corner in the middle of an edge)
                                    for (float dIn = 0.10f; dIn <= 1.501f; dIn += 0.05f)
                                    {
                                        if (!Highest(edgeW - o3 * dIn + Vector3.up * 0.3f, 0.6f, out var hq) || hq.collider.gameObject.layer == CityWorld.SolidLayer) break;
                                        if (hq.collider.name != "Roads") continue;
                                        float atEnd = Highest(edgeW - o3 * (dIn - 0.10f) + Vector3.up * 1.2f, 4f, out var hb) && !Claimed(hb) ? hq.point.y - hb.point.y : 0f;
                                        float past = hq.point.y - h0.point.y - RoadsideRules.SteepestRecoverableSlope * dIn;
                                        step = Mathf.Max(atEnd, past);
                                        measured = past > atEnd
                                            ? $"5 cm past the edge, {dIn:0.00} m past the tarmac, beyond a 1V:4H fall from it"
                                            : $"5-10 cm past the tarmac, which ends {dIn - 0.05f:0.00} m inside the edge";
                                        break;
                                    }
                                }
                                if (step > RoadsideRules.EdgeDropFailM)
                                {
                                    vergeStepFails++;
                                    lipNotes.Add((step, $"LIP   {step:0.00} m {measured}, e{ei} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} s={s:0.0}/{e.length:0} side {(side < 0 ? "L" : "R")} hw {hw:0.00} on {HitPath(h0)} at ({edgeW.x:0.0},{edgeW.z:0.0}) tile {tx},{tz} {NodeNote(e, s)}{CityMeshes.DescribeClip(map, trims, e, s)}{CityMeshes.DescribeSide(map, trims, e, s, side)}"));
                                }
                                // A LEDGE a few hands out (a survey, not a check
                                // yet): walking out over the verge, ground that
                                // steps LedgeMinM to a metre down onto whatever
                                // lies below it within 1.5 m. No check here sees
                                // one: the rail census reads the drop 1.5 m out
                                // and a metre passes, the body-box ray below stands
                                // down where a road lies a metre out, and the
                                // builder's DropFrom calls a step onto a road under
                                // OpenDropM a kerb. A harness walk found 138 on the
                                // verge code before round three and 81 after (a
                                // 0.92 m ledge 0.9 m off e7753, where the fin the
                                // FACE check failed had stood in front of it).
                                {
                                    float prevY = float.NaN;
                                    bool prevGround = false;
                                    for (float dd = 0.05f; dd <= 1.5f; dd += 0.05f)
                                    {
                                        if (!Highest(edgeW + o3 * dd + Vector3.up * 1.2f, 4f, out var hs) || hs.collider.gameObject.layer == CityWorld.SolidLayer) break;
                                        float fall = prevY - hs.point.y;
                                        if (prevGround && fall > LedgeMinM && fall < RoadsideRules.OpenDropM)
                                        {
                                            ledges++;
                                            ledgeNotes.Add((fall, $"LEDGE {fall:0.00} m down {dd:0.00} m past the edge onto {HitPath(hs)}, e{ei} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} s={s:0.0}/{e.length:0} side {(side < 0 ? "L" : "R")} at ({edgeW.x:0.0},{edgeW.z:0.0}) tile {tx},{tz} {NodeNote(e, s)}{CityMeshes.DescribeClip(map, trims, e, s)}{CityMeshes.DescribeSide(map, trims, e, s, side)}"));
                                            break;
                                        }
                                        if (hs.collider.name == "Roads" && dd > 0.1f) break;
                                        prevY = hs.point.y;
                                        prevGround = hs.collider.name == "Ground";
                                    }
                                }
                                // the body box coming back: a ray at its lowest
                                // clearance over the ground a metre out
                                if (!Highest(edgeW + o3 * 1.0f + Vector3.up * 1.2f, 6f, out var h1) || Claimed(h1)) continue;
                                var from = new Vector3(edgeW.x + outw.x, h1.point.y + RoadsideRules.CarClearanceFloorM + 0.01f, edgeW.z + outw.y);
                                bool back = Physics.queriesHitBackfaces;
                                Physics.queriesHitBackfaces = true;
                                try
                                {
                                    foreach (var hf in Physics.RaycastAll(from, -o3, 1.0f, ~0, QueryTriggerInteraction.Ignore))
                                    {
                                        if (hf.collider.gameObject.layer == CityWorld.SolidLayer || Mathf.Abs(hf.normal.y) >= 0.7f) continue;
                                        faceFails++;
                                        faceNotes.Add((hf.point.y - h1.point.y, $"FACE  normal.y {hf.normal.y:0.00} {Vector3.Distance(from, hf.point):0.00} m in from 1 m out, e{ei} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} s={s:0.0}/{e.length:0} side {(side < 0 ? "L" : "R")} on {HitPath(hf)} at ({hf.point.x:0.0},{hf.point.z:0.0}) y {hf.point.y - y:+0.00;-0.00}, ground 1 m out {h1.point.y - y:+0.00;-0.00} on {HitPath(h1)}, tile {tx},{tz} {NodeNote(e, s)}{CityMeshes.DescribeClip(map, trims, e, s)}{CityMeshes.DescribeSide(map, trims, e, s, side)}"));
                                        break;
                                    }
                                }
                                finally { Physics.queriesHitBackfaces = back; }
                            }
                        }
                        CloseRun(0); CloseRun(1);
                    }

                    // ---- fan chords ------------------------------------------
                    var nodes = new HashSet<int>();
                    foreach (var ei in edges) { nodes.Add(map.edges[ei].a); nodes.Add(map.edges[ei].b); }
                    foreach (var n in nodes)
                    {
                        if (!In(map.nodes[n])) continue;
                        bool onStructure = CityMeshes.FanPerimeter(map, trims, n, chords);
                        foreach (var (a, b, outw) in chords)
                        {
                            float len = Vector3.Distance(a, b);
                            float openRun = 0f;
                            var along = new Vector2(b.x - a.x, b.z - a.z).normalized;
                            for (float d = 0.5f; d < len; d += 1f)
                            {
                                if (RailProbe(Vector3.Lerp(a, b, d / len), outw, along) == 2) { openRun += 1f; dropMetres += 1f; }
                            }
                            if (openRun >= RoadsideRules.DeckRailGapFailM)
                            {
                                string kind = onStructure ? "fan chord on structure" : "fan chord";
                                gapRuns++; gapMetres += openRun;
                                gapByKind.TryGetValue(kind, out float gm); gapByKind[kind] = gm + openRun;
                                notes.Add((openRun, $"OPEN  {openRun:0} m of {kind} over a drop at node {n} ({a.x:0},{a.z:0})-({b.x:0},{b.z:0}) tile {tx},{tz}"));
                            }
                        }
                    }

                    // ---- gore noses ------------------------------------------
                    foreach (var (a, b, fwd, elevNose) in tmC.goreNoses)
                    {
                        float len = Vector3.Distance(a, b);
                        var outw = new Vector2(fwd.x, fwd.z);
                        var along = new Vector2(b.x - a.x, b.z - a.z).normalized;
                        float openRun = 0f;
                        for (float d = 0.25f; d < len; d += 0.5f)
                            if (RailProbe(Vector3.Lerp(a, b, d / len), outw, along) == 2) { openRun += 0.5f; dropMetres += 0.5f; }
                        if (openRun >= RoadsideRules.DeckRailGapFailM)
                        {
                            string kind = elevNose ? "gore nose on structure" : "gore nose";
                            gapRuns++; gapMetres += openRun;
                            gapByKind.TryGetValue(kind, out float gm); gapByKind[kind] = gm + openRun;
                            notes.Add((openRun, $"OPEN  {openRun:0.0} m of {kind} over a drop ({a.x:0},{a.z:0})-({b.x:0},{b.z:0}) tile {tx},{tz}"));
                        }
                    }

                    // ---- pits ----------------------------------------------
                    float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
                    for (int iz = 0; iz <= CityMeshes.GroundRes; iz++)
                        for (int ix = 0; ix <= CityMeshes.GroundRes; ix++)
                        {
                            float x = min.x + ix * cell, z = min.y + iz * cell;
                            float gy = CityElevation.Ground(map, x, z, out var gt);
                            if (gt.nearEdge < 0 || float.IsNaN(gt.nearFloor) || gt.nearFloor - gy <= 0.5f) continue;
                            pits++;
                            string cause;
                            if (!float.IsNaN(gt.deckCap) && Mathf.Abs(gy - gt.deckCap) < 1e-3f) cause = "dug under a deck";
                            else if (!float.IsNaN(gt.deckProtect) && Mathf.Abs(gy - gt.deckProtect) < 1e-3f) cause = "held under a deck's pavement";
                            else if (!float.IsNaN(gt.protect) && Mathf.Abs(gy - gt.protect) < 1e-3f) cause = "held under a lower road's pavement (conflict)";
                            else if (gt.dem < CityElevation.BaseY(x, z) - 0.05f) cause = "water";
                            else { cause = "UNEXPLAINED"; pitsUnexplained++; }
                            pitByCause.TryGetValue(cause, out int pc); pitByCause[cause] = pc + 1;
                            if (cause == "UNEXPLAINED" || notes.Count < 400)
                                notes.Add((0.1f + gt.nearFloor - gy, $"PIT   {gt.nearFloor - gy:0.00} m under e{gt.nearEdge} '{map.edges[gt.nearEdge].name}' ({gt.nearDist:0.0} m past its pavement) at ({x:0},{z:0}) tile {tx},{tz}: {cause}{(gt.protectEdge >= 0 && cause.StartsWith("held under a lower") ? $" e{gt.protectEdge} '{map.edges[gt.protectEdge].name}'" : "")}"));
                        }
                }
            }
            finally
            {
                foreach (var k in new List<long>(live.Keys)) DropTile(k);
                Object.DestroyImmediate(root);
            }

            Line($"roadside audit: {probeTiles.Count} tiles (routes + {RoadsideTopElevatedTiles} most elevated) built {vergeBuilt / 1000f:0.0} km of verge, {railBuilt / 1000f:0.0} km of rail, {nosesBuilt} gore noses; " +
                 $"{vergePoints} verge points, {railPoints} rail probe points, {dropMetres:0} m of edge over a drop > {RoadsideRules.OpenDropM} m without a barrier");
            foreach (var kv in gapByKind) Line($"    open {kv.Key}: {kv.Value:0} m");
            var pitLine = new StringBuilder($"    pits (lattice > 0.5 m under a grounded ribbon's design within {CityElevation.PitReachM} m): {pits}");
            foreach (var kv in pitByCause) pitLine.Append($"; {kv.Key} {kv.Value}");
            Line(pitLine.ToString());
            Check(vergeStepFails == 0, "every grounded edge meets its verge within " + RoadsideRules.EdgeDropFailM + " m (roadside audit)",
                  $"{vergeStepFails} of {vergePoints} points step more");
            Check(faceFails == 0, "no face stops the body box coming back onto a grounded edge (roadside audit)", faceFails);
            Check(gapRuns == 0, "no edge over a drop past " + RoadsideRules.OpenDropM + " m is without a barrier: decks, approaches, ledges, fan chords, gore noses (rail census)",
                  $"{gapRuns} runs, {gapMetres:0} m");
            Check(pitsUnexplained == 0, "every pit beside a grounded ribbon has a rule that put it there (pit census)", pitsUnexplained);
            // Not checks yet. What is left (2026-09-14) is roads drawn into
            // each other where the squeeze may not split them — junction arms
            // and host/branch pairs a level apart: the link e14103 half a
            // metre over North Davidson Street, the Independence Expressway
            // coming down beside Albemarle Road on its retaining wall — a
            // building wall standing in a street (the footprints'), and rails
            // a hand inside a lane line at polyline bends; each run below
            // names which.
            var laneLine = new StringBuilder($"    lane survey (not a check): {lanePoints} lane probes on the roadside tiles; land the first thing over a lane {laneLand}; " +
                                             $"something solid within {RoadsideRules.CarBandM} m over a lane {laneBand}");
            foreach (var kv in bandByClass) laneLine.Append($", {kv.Key} {kv.Value}");
            laneLine.Append($" (a solid top over 0.35 m up, seen from 3 m over the lane: {laneSolid})");
            Line(laneLine.ToString());
            Line($"    ledge survey (not a check): {ledges} of {vergePoints} verge points step {LedgeMinM}-{RoadsideRules.OpenDropM} m down within 1.5 m past a grounded edge, with no barrier");
            // every OPEN run (they are metres, and few), then the worst of each
            // other kind, one line per edge and side
            void Print(List<(float sev, string what)> list, int cap, string title, bool perEdge)
            {
                if (list.Count == 0) return;
                list.Sort((p, q) => q.sev.CompareTo(p.sev));
                var shown = new HashSet<string>();
                var lines = new List<string>(cap);
                foreach (var (sev, what) in list)
                {
                    if (perEdge)
                    {
                        var edge = System.Text.RegularExpressions.Regex.Match(what, @" (e\d+|node \d+) ");
                        var side = System.Text.RegularExpressions.Regex.Match(what, @" side [LR]");
                        if (edge.Success && !shown.Add(edge.Value + side.Value)) continue;
                    }
                    lines.Add(what);
                    if (lines.Count >= cap) break;
                }
                Line($"    -- {title}: {list.Count}{(lines.Count < list.Count ? $", {lines.Count} shown{(perEdge ? " (the worst per edge and side)" : "")}" : "")}");
                foreach (var what in lines) Line("    " + what);
            }
            // two OPEN runs on one edge side are two holes, not one line
            var openNotes = notes.FindAll(n => n.what.StartsWith("OPEN"));
            var pitNotes = notes.FindAll(n => n.what.StartsWith("PIT"));
            Print(openNotes, 60, "open edges over a drop", false);
            Print(faceNotes, 30, "faces in the body box's way", true);
            Print(lipNotes, 30, "lips past a grounded edge", true);
            Print(ledgeNotes, 30, "ledges past a grounded edge (survey)", true);
            int bandRuns = laneRuns.FindAll(r => r.kind == "band").Count;
            Line($"    -- land or something solid within a car's height over a lane: {laneRuns.Count} runs ({bandRuns} solid, {laneRuns.Count - bandRuns} land), every one");
            foreach (var r in laneRuns) Line("    " + r.Describe());
            Print(pitNotes, 10, "pits", true);
        }

        /// <summary>The least step down past a grounded edge the ledge survey
        /// lists: well past a wheel's lip, short of the rail census's metre.</summary>
        const float LedgeMinM = 0.3f;
        /// <summary>The lowest point over a lane the lane survey's column
        /// starts at: a kerb's inch and the rail's buried foot sit below it.</summary>
        const float LaneBandFloorM = 0.15f;
        /// <summary>Half the lane survey column's width. The lane line is
        /// 0.55 m in from the drawn edge and a rail's traffic face RailW
        /// inside it, so a 0.1 m column meets a rail only where it stands
        /// in the lane.</summary>
        const float LaneColumnM = 0.1f;

        /// <summary>One lane-survey finding: consecutive probes along one lane
        /// of one edge that met the same kind of thing, merged.</summary>
        class LaneRun
        {
            public string kind, cls, what, owner, edgeName;
            public int edge, lane, tx, tz, count;
            public float s0, s1;
            public string Describe() =>
                $"LANE  {(kind == "land" ? "land over" : "solid in")} lane{lane} of e{edge} {edgeName} s={s0:0}{(s1 > s0 + 0.5f ? $"..{s1:0}" : "")} ({count} probe{(count > 1 ? "s" : "")}) tile {tx},{tz}: {what}; {cls}{owner}";
        }

        static void AddLaneRun(List<LaneRun> runs, string kind, CityMap.Edge e, int lane, float s, int tx, int tz, string what, string cls, string owner)
        {
            for (int i = runs.Count - 1; i >= 0 && i >= runs.Count - 6; i--)
            {
                var r = runs[i];
                if (r.kind != kind || r.edge != e.index || r.lane != lane || r.cls != cls || s - r.s1 > 2.5f) continue;
                r.s1 = s; r.count++;
                return;
            }
            runs.Add(new LaneRun
            {
                kind = kind, edge = e.index, edgeName = $"'{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")}", lane = lane,
                s0 = s, s1 = s, tx = tx, tz = tz, what = what, cls = cls, owner = owner, count = 1,
            });
        }

        /// <summary>
        /// Whose geometry stands at a point over a lane: the nearest drawn
        /// ribbon edge within a rail's reach — the lane's own (its rail drawn
        /// inside its lanes), another road's at the same height (two roads
        /// drawn into each other), another road's at another height (a road
        /// over or under this one in plan, closer than a car) — or a fan
        /// chord. Called for findings only (it rebuilds sections).
        /// </summary>
        static string LaneOwner(CityMap map, CityMeshes.Trims trims, int laneEdge, Vector2 p2, float laneY, out string cls)
        {
            cls = "nobody's edge within 0.8 m";
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(p2 - Vector2.one * 20f, p2 + Vector2.one * 20f, segs);
            var near = new HashSet<int>();
            foreach (var packed in segs) near.Add(packed >> 12);
            float best = 0.8f; int bestE = -1, bestSide = 0; float bestS = 0f;
            foreach (var oi in near)
            {
                var o = map.edges[oi];
                CityElevation.ProjectOn(o, p2, out float so);
                if (so <= 0.01f || so >= o.length - 0.01f) continue;
                var po = o.PointAt(so); var to = o.TangentAt(so);
                if (Mathf.Abs(Vector2.Dot(p2 - po, to)) > 1.5f) continue;
                float lat = Vector2.Dot(p2 - po, new Vector2(-to.y, to.x));
                CityMeshes.LaneExtents(map, trims, o, so, out float hl, out float hr);
                float off = Mathf.Abs(Mathf.Abs(lat) - (lat >= 0f ? hr : hl));
                if (off >= best) continue;
                best = off; bestE = oi; bestS = so; bestSide = lat >= 0f ? 1 : -1;
            }
            string desc = "";
            if (bestE >= 0)
            {
                var o = map.edges[bestE];
                float dy = o.YAt(bestS) - laneY;
                cls = bestE == laneEdge ? "its own edge" : Mathf.Abs(dy) < 0.1f ? "another road's edge at its height" : "another road's edge at another height";
                desc = $": e{bestE} '{o.name}'{(o.link ? " L" : "")} side {(bestSide < 0 ? "L" : "R")} s={bestS:0}, its surface {dy:+0.00;-0.00} m{CityMeshes.DescribeSide(map, trims, o, bestS, bestSide)}";
            }
            var chords = new List<(Vector3 a, Vector3 b, Vector2 outward)>();
            var nodes = new HashSet<int>();
            foreach (var oi in near) { nodes.Add(map.edges[oi].a); nodes.Add(map.edges[oi].b); }
            foreach (var n in nodes)
            {
                if (!trims.patch[n] || Vector2.Distance(map.nodes[n], p2) > 30f) continue;
                CityMeshes.FanPerimeter(map, trims, n, chords);
                foreach (var (a, b, _) in chords)
                {
                    var A = new Vector2(a.x, a.z); var d = new Vector2(b.x, b.z) - A;
                    float t = Mathf.Clamp01(Vector2.Dot(p2 - A, d) / Mathf.Max(d.sqrMagnitude, 1e-6f));
                    float dist = Vector2.Distance(p2, A + d * t);
                    if (dist >= best) continue;
                    best = dist; cls = "a fan chord";
                    desc = $": node {n}'s chord ({a.x:0},{a.z:0})-({b.x:0},{b.z:0}), its surface {Mathf.Lerp(a.y, b.y, t) - laneY:+0.00;-0.00} m";
                }
            }
            return desc;
        }

        // ==================================================================
        /// <summary>
        /// THE JUNCTION MOUTHS (2026-09-14). The drive audit's lanes run from
        /// trim to trim and the roadside audit walks a fan's chords; neither
        /// looked INSIDE a fan, where Tyvola Road's lanes ran a metre past
        /// their mouth onto the land a metre down (a link's corner had sorted
        /// between Tyvola's two and split the fan). On every tile the audit
        /// probes from, every arm of every fan is asked, a lane line a metre
        /// across its drawn width, 0.3 m and 1 m back from its mouth toward the
        /// node: the first thing under it must be road. Land or nothing there
        /// fails. A road a level away, or a solid, is listed (overlapping
        /// ribbons, the lane survey's to count).
        /// </summary>
        class FanMouthTally
        {
            public int fans, probes, noRoad, offLevel, solid;
            public readonly HashSet<int> nodes = new HashSet<int>();
            public readonly List<(float sev, string what)> notes = new List<(float, string)>();
        }
        static FanMouthTally fanMouths;
        static readonly float[] FanMouthInsets = { 0.3f, 1.0f };
        /// <summary>A fan surface this far off the arm-to-node height is some
        /// other road's (the drive audit's OFF threshold).</summary>
        const float FanMouthOffM = 0.35f;

        /// <summary>Probe the fans whose nodes are on tile (tx, tz). Its 3x3
        /// neighbourhood must be standing, the tile itself built last (the
        /// clip table is what LaneExtents reads).</summary>
        static void FanMouths(CityMap map, CityMeshes.Trims trims, int tx, int tz)
        {
            var t = fanMouths;
            if (t == null) return;
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            var max = min + Vector2.one * CityMeshes.TileSize;
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(min, max, segs);
            var nodes = new SortedSet<int>();
            foreach (var packed in segs) { var e = map.edges[packed >> 12]; nodes.Add(e.a); nodes.Add(e.b); }
            foreach (var n in nodes)
            {
                var np = map.nodes[n];
                if (!trims.patch[n] || np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y) continue;
                if (!t.nodes.Add(n)) continue;   // a tile both audits probe counts once
                t.fans++;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    float trim = trims.TrimAt(e, n);
                    float at = e.a == n ? trim : e.length - trim;
                    var tan = e.TangentAt(at);
                    var outDir = e.a == n ? tan : -tan;
                    var right = new Vector2(-tan.y, tan.x);
                    CityMeshes.LaneExtents(map, trims, e, at, out float hwL, out float hwR);
                    if (hwL + hwR < 1.2f) continue;   // clipped into its host: no lanes of its own here
                    var p = e.PointAt(at);
                    float yArm = e.YAt(at);
                    foreach (float inset in FanMouthInsets)
                    {
                        if (inset > trim - 0.3f) continue;
                        float yExp = Mathf.Lerp(yArm, map.nodeY[n], inset / Mathf.Max(trim, 0.01f));
                        float l0 = -(hwL - 0.5f), l1 = hwR - 0.5f;
                        int count = Mathf.Max(2, Mathf.CeilToInt(l1 - l0) + 1);
                        for (int q = 0; q < count; q++)
                        {
                            float lat = Mathf.Lerp(l0, l1, (float)q / (count - 1));
                            var w2 = p + right * lat - outDir * inset;
                            var w = new Vector3(w2.x, yExp + 3f, w2.y);
                            t.probes++;
                            string what; float sev;
                            if (!Physics.Raycast(w, Vector3.down, out var h, 6.5f, ~0, QueryTriggerInteraction.Ignore))
                            { t.noRoad++; what = "nothing under it"; sev = 9f; }
                            else
                            {
                                float d = h.point.y - yExp;
                                string path = (h.collider.transform.parent != null ? h.collider.transform.parent.name + "/" : "") + h.collider.name;
                                if (h.collider.name == "Ground") { t.noRoad++; what = $"land {d:+0.00;-0.00} m ({path})"; sev = 5f + Mathf.Abs(d); }
                                else if (h.collider.gameObject.layer == CityWorld.SolidLayer) { t.solid++; what = $"a solid {d:+0.00;-0.00} m ({path})"; sev = 2f; }
                                else if (Mathf.Abs(d) > FanMouthOffM) { t.offLevel++; what = $"a road {d:+0.00;-0.00} m off ({path})"; sev = Mathf.Abs(d); }
                                else continue;
                            }
                            t.notes.Add((sev, $"FAN   node {n} arm e{ei} '{e.name}'{(e.link ? " L" : "")} {inset:0.0} m back from the mouth, lane line {lat:+0.0;-0.0}: {what} at ({w.x:0.0},{w.z:0.0}) tile {tx},{tz} deg{map.nodeEdges[n].Count} trim {trim:0.0}"));
                        }
                    }
                }
            }
        }

        static void ReportFanMouths()
        {
            var t = fanMouths;
            if (t == null) return;
            Line($"fan mouth probe: {t.fans} fans on the probed tiles, {t.probes} probes; land or nothing {t.noRoad}, a road a level away {t.offLevel}, a solid {t.solid}");
            Check(t.noRoad == 0, "every lane mouth at a junction fan has road under it (fan mouth probe)", $"{t.noRoad} of {t.probes} probes");
            t.notes.Sort((p, q) => q.sev.CompareTo(p.sev));
            var shown = new HashSet<string>();
            int lines = 0;
            foreach (var (sev, what) in t.notes)
            {
                // one line per node and arm, its worst probe
                int cut = what.IndexOf(" m back");
                string key = what.Substring(0, what.LastIndexOf(' ', cut - 1));
                if (!shown.Add(key)) continue;
                Line("    " + what);
                if (++lines >= 30) break;
            }
        }

        /// <summary>No station-to-station grade past 16% outside a sub-30 m
        /// sliver; the worst dozen dumped with their seats, nodes and
        /// crossings, so a failure explains itself.</summary>
        static void GradeAudit(CityMap map)
        {
            var mask = CityElevation.EnforcedCrossings;
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
            if (gradeFails == 0) return;
            badG.Sort((a, b) => b.g.CompareTo(a.g));
            var shownEdges = new HashSet<int>();
            foreach (var (g, e, at) in badG)
            {
                if (!shownEdges.Add(e.index)) continue;
                if (shownEdges.Count > 12) break;
                var p = e.PointAt(at);
                Line($"    grade {(g * 100f):0}% on e{e.index} '{e.name}' L{e.layer} cls{e.cls} len{e.length:0} at s={at:0} ({p.x:0},{p.y:0}) {LatLon(p.x, p.y)}" +
                     (e.bridge ? " BRIDGE" : "") + (e.link ? " LINK" : ""));
                var sb = new StringBuilder("      y:");
                for (int i = 0; i < e.stY.Length; i++)
                    sb.Append(' ').Append(e.stY[i].ToString("0.0")).Append(e.stElev[i] ? "^" : "").Append(e.SeatedAt(i) ? "s" : "");
                Line(sb.ToString());
                Line("      seats:" + CityElevation.DescribeSeats(e.index));
                foreach (var n in new[] { e.a, e.b })
                {
                    var nb = new StringBuilder($"      node {n} y={map.nodeY[n]:0.0} base={CityElevation.BaseY(map.nodes[n].x, map.nodes[n].y):0.0}:");
                    foreach (var oi in map.nodeEdges[n])
                    {
                        var o = map.edges[oi];
                        bool atA = o.a == n;
                        float endY = atA ? o.stY[0] : o.stY[o.stY.Length - 1];
                        bool endSeat = o.SeatedAt(atA ? 0 : o.stY.Length - 1);
                        nb.Append($" e{oi}'{o.name}'{(o.link ? "L" : "")}{(o.bridge ? "B" : "")} len{o.length:0} end{endY:0.0}{(endSeat ? "s" : "")}");
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

        /// <summary>Headless entry for the overlap census alone: the graph,
        /// the solve and the trims, no tiles. Seconds, not minutes.</summary>
        public static void RunOverlaps()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); Finish(); return; }
            GradeAudit(map);
            OverlapCensus(map, CityMeshes.ComputeTrims(map), writeCsv: true);
            Finish();
        }

        /// <summary>Game (x, z) to latitude/longitude, for checking a spot
        /// against satellite imagery.</summary>
        public static string LatLon(float x, float z) =>
            $"{35.18456 + z / 111132.0:0.000000},{-80.81770 + x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)):0.000000}";

        /// <summary>
        /// THE STAIRCASE CENSUS. Two paved ribbons that overlap in plan must
        /// either be one surface (the same height, give or take a seam) or a
        /// grade separation (one a clearance above the other). Anything in
        /// between is a road standing up out of another road's lanes — "an
        /// entrance ramp going up through the centre of a road like a staircase
        /// in the centre of a house". City-wide, every 4 m of every ribbon,
        /// against every other ribbon it overlaps; junction patches excused
        /// (the fan owns that ground). Returns the overlapping metres.
        /// </summary>
        public static float OverlapCensus(CityMap map, CityMeshes.Trims trims, bool writeCsv)
            => OverlapCensus(map, trims, writeCsv, null, null);

        public static float OverlapCensus(CityMap map, CityMeshes.Trims trims, bool writeCsv, List<OverlapPair> worstOut)
            => OverlapCensus(map, trims, writeCsv, worstOut, null);

        /// <summary>A pair of overlapping ribbons, for the preview: the two
        /// edges, where the worst sample is, how long and how far apart.</summary>
        public struct OverlapPair { public int e, o; public Vector2 at; public float len, worst; public bool branch; }

        public static float OverlapCensus(CityMap map, CityMeshes.Trims trims, bool writeCsv, List<OverlapPair> worstOut,
                                          OverlapStats stats)
        {
            const float Step = 4f, MinDy = 0.25f, MaxDy = CityElevation.ClearanceM - 0.4f;
            var csv = writeCsv ? new StringBuilder("e,s,o,sO,dy,dist,dot,hwE,hwO,shared,branch,crossing,link_e,link_o,cls_e,cls_o,elev_e,elev_o,bridge_e,bridge_o,x,z,latlon,name_e,name_o\n") : null;
            var crossPairs = new HashSet<long>();
            foreach (var c in map.crossings) crossPairs.Add(PairKey(c.over, c.under));
            var segs = new HashSet<int>();
            var bestPer = new Dictionary<int, (float dist, float sO)>();
            float metres = 0f, metresBranch = 0f, metresBig = 0f;
            int samples = 0;
            var pairs = new Dictionary<long, (float len, float worst, int e, int o, Vector2 at, bool branch)>();
            foreach (var e in map.edges)
            {
                float s0 = trims.atA[e.index], s1 = e.length - trims.atB[e.index];
                for (float s = s0; s <= s1; s += Step)
                {
                    var p = e.PointAt(s);
                    float hwE = trims.HalfWidthAt(e, s);
                    float y = e.YAt(s);
                    segs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * (hwE + 13f), p + Vector2.one * (hwE + 13f), segs);
                    bestPer.Clear();
                    foreach (var packed in segs)
                    {
                        int oi = packed >> 12, si = packed & 0xFFF;
                        if (oi <= e.index) continue;
                        var o = map.edges[oi];
                        Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                        float dist = Vector2.Distance(p, a + d * t);
                        float sO = o.s[si] + Mathf.Sqrt(L2) * t;
                        if (!bestPer.TryGetValue(oi, out var bp) || dist < bp.dist) bestPer[oi] = (dist, sO);
                    }
                    foreach (var kv in bestPer)
                    {
                        var o = map.edges[kv.Key];
                        float sO = kv.Value.sO, dist = kv.Value.dist;
                        if (sO < trims.atA[o.index] || sO > o.length - trims.atB[o.index]) continue;
                        // Off the END of the other road is not inside it: a
                        // sample just past a mitred node projects onto the
                        // continuing edge's first point and reads its height.
                        if (sO < 0.5f || sO > o.length - 0.5f) continue;
                        float hwO = trims.HalfWidthAt(o, sO);
                        if (dist > hwE + hwO - 0.5f) continue;
                        float dy = o.YAt(sO) - y;
                        // the fan at a shared node owns the ground around it
                        int shared = e.a == o.a || e.a == o.b ? e.a : e.b == o.a || e.b == o.b ? e.b : -1;
                        bool branch = trims.branchA[e.index] == o.index || trims.branchB[e.index] == o.index ||
                                      trims.branchA[o.index] == e.index || trims.branchB[o.index] == e.index;
                        // A branch beside its own host is one pavement at ANY
                        // height difference; only an unrelated pair may be a
                        // separation.
                        if (Mathf.Abs(dy) < MinDy || (!branch && Mathf.Abs(dy) > MaxDy)) continue;
                        if (shared >= 0)
                        {
                            var np = map.nodes[shared];
                            float trim = Mathf.Max(trims.TrimAt(e, shared), trims.TrimAt(o, shared));
                            if (Vector2.Distance(p, np) < trim + hwO + 2f) continue;
                        }
                        float dot = Mathf.Abs(Vector2.Dot(e.TangentAt(s), o.TangentAt(sO)));
                        samples++;
                        metres += Step;
                        if (branch && (e.link || o.link))
                        {
                            if (stats != null) stats.branchMetres += Step;
                            if (stats != null && Mathf.Abs(dy) > BranchAttachDy) stats.branchPastAttach += Step;
                        }
                        if (e.link != o.link && dot > 0.85f) metresBranch += Step;
                        if (Mathf.Abs(dy) > 1f) metresBig += Step;
                        long key = PairKey(e.index, o.index);
                        pairs.TryGetValue(key, out var pr);
                        bool worse = Mathf.Abs(dy) > pr.worst;
                        pairs[key] = (pr.len + Step, Mathf.Max(pr.worst, Mathf.Abs(dy)), e.index, o.index, worse ? p : pr.at, branch);
                        csv?.Append($"{e.index},{s:0.0},{o.index},{sO:0.0},{dy:0.00},{dist:0.0},{dot:0.00},{hwE:0.0},{hwO:0.0},{shared},{(branch ? 1 : 0)},{(crossPairs.Contains(PairKey(e.index, o.index)) ? 1 : 0)}," +
                                    $"{(e.link ? 1 : 0)},{(o.link ? 1 : 0)},{e.cls},{o.cls},{(e.ElevatedAt(s) ? 1 : 0)},{(o.ElevatedAt(sO) ? 1 : 0)}," +
                                    $"{(e.bridge ? 1 : 0)},{(o.bridge ? 1 : 0)},{p.x:0},{p.y:0},\"{LatLon(p.x, p.y)}\",\"{e.name}\",\"{o.name}\"\n");
                    }
                }
            }
            int bigPairs = 0;
            var worst = new List<(float score, long key)>();
            foreach (var kv in pairs)
            {
                if (kv.Value.worst > 1f) bigPairs++;
                worst.Add((kv.Value.len * kv.Value.worst, kv.Key));
            }
            worst.Sort((a, b) => b.score.CompareTo(a.score));
            Line($"overlap census: {metres:0} m of ribbon inside another ribbon at a height between {MinDy} m and {MaxDy} m " +
                 $"({samples} samples, {pairs.Count} pairs, {bigPairs} pairs past 1 m; {metresBranch:0} m ramp-beside-road, {metresBig:0} m past 1 m)");
            for (int i = 0; i < Mathf.Min(20, worst.Count); i++)
            {
                var v = pairs[worst[i].key];
                var e = map.edges[v.e]; var o = map.edges[v.o];
                Line($"    {v.len:0} m, worst {v.worst:0.00} m: e{e.index} '{e.name}'{(e.link ? " L" : "")} cls{e.cls} / e{o.index} '{o.name}'{(o.link ? " L" : "")} cls{o.cls} at ({v.at.x:0},{v.at.y:0}) {LatLon(v.at.x, v.at.y)}");
            }
            if (writeCsv)
                for (int i = 0; i < Mathf.Min(6, worst.Count); i++)
                {
                    var v = pairs[worst[i].key];
                    Line("  pair " + i + ":");
                    DescribeEdge(map, trims, map.edges[v.e]);
                    DescribeEdge(map, trims, map.edges[v.o]);
                }
            if (csv != null)
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_overlaps.csv"), csv.ToString());
            if (stats != null) stats.metres = metres;
            if (worstOut != null)
                foreach (var (score, key) in worst)
                {
                    var v = pairs[key];
                    worstOut.Add(new OverlapPair { e = v.e, o = v.o, at = v.at, len = v.len, worst = v.worst, branch = v.branch });
                }
            return metres;
        }

        static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Everything the solver knew about one edge: its tags, its
        /// profile station by station (^ = structure), both nodes with every
        /// arm's end height, its branch hosts, and every crossing it is in.</summary>
        public static void DescribeEdge(CityMap map, CityMeshes.Trims trims, CityMap.Edge e)
        {
            var mask = CityElevation.EnforcedCrossings;
            Line($"    e{e.index} '{e.name}'{(e.link ? " LINK" : "")}{(e.bridge ? " BRIDGE" : "")}{(e.tunnel ? " TUNNEL" : "")} cls{e.cls} layer{e.layer} len{e.length:0} w{e.width:0.0} " +
                 $"branchA e{trims.branchA[e.index]} branchB e{trims.branchB[e.index]} trims {trims.atA[e.index]:0.0}/{trims.atB[e.index]:0.0}");
            var sb = new StringBuilder("      y:");
            for (int i = 0; i < e.stY.Length; i++)
            {
                var p = e.PointAt(e.stS[i]);
                sb.Append(' ').Append(e.stY[i].ToString("0.0")).Append(e.stElev[i] ? "^" : "")
                  .Append('(').Append((e.stY[i] - CityElevation.BaseY(p.x, p.y)).ToString("+0.0;-0.0")).Append(')');
            }
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
                Line($"      {(c.over == e.index ? "OVER" : "UNDER")} e{o.index} '{o.name}'{(o.link ? "L" : "")} layer{o.layer} at s={sc:0} ({c.at.x:0},{c.at.y:0}){(c.forced ? "" : " unforced")}{(CityElevation.TrenchedCrossings[ci] ? " TRENCH" : "")}{(mask != null && !mask[ci] ? " pruned" : "")}");
            }
        }

        static int VCount(Mesh m) => m == null ? 0 : m.vertexCount;

        static void Check(bool ok, string what, object detail = null)
        {
            if (!ok) failures++;
            Line((ok ? "  ok  " : "  FAIL ") + what + (detail != null ? " — " + detail : ""));
        }

        static void Fail(string what) { failures++; Line("  FAIL " + what); }
        static void Line(string s) { outLog?.AppendLine(s); Debug.Log("[CityAudit] " + s); }

        static void Finish()
        {
            outLog.AppendLine(failures == 0 ? "CITY AUDIT OK" : $"CITY AUDIT: {failures} FAILURES");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "city_audit.txt"), outLog.ToString());
        }
    }
}
