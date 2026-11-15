using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD NETWORK REVIEW (2026-11-15, the owner: "Please review all roads
    /// for accuracy ... I plan to start adding more racing tracks in Charlotte
    /// today once roads are accurate"). Two things only the game's own code can
    /// say (the data-level half is tools-side: the round's net_audit.mjs reads
    /// charlotte_city.bytes against the OSM extract):
    ///
    ///   NETWORK   what each junction is CONTROLLED by once CitySignals has
    ///             decided (OSM's node, a signal snapped from an approach, a
    ///             warrant), against the OSM signal nodes on the game's roads
    ///             (PSX_NET_OSMSIGNALS: a file of "x z lat lon" rows); the
    ///             freeway divider plan's km by kind; the median cells; the
    ///             lane profile census; the turn pockets' state.
    ///             -executeMethod PSXRacing.EditorTools.CityAudit.RunNetwork
    ///             -> city_network.txt
    ///   SAMPLE    THE ROUND'S DRIVABILITY BASELINE: the drive audit and the
    ///             invisible-collider audit on a fixed sample of 100 tiles in
    ///             five 5 x 4 boxes of mixed kinds (<see cref="SampleBoxes"/>;
    ///             PSX_ROADS_SAMPLE=kind,kind picks some), every road on them
    ///             counted. The same call after a merge gives the numbers to
    ///             compare.
    ///             -executeMethod PSXRacing.EditorTools.CityAudit.RunRoadsSample
    ///             -> city_roads_sample.txt
    /// </summary>
    public static partial class CityAudit
    {
        /// <summary>The sample: kind, then the box in whole tiles (x0, z0 .. x1, z1 inclusive).</summary>
        public static readonly (string kind, int x0, int z0, int x1, int z1)[] SampleBoxes =
        {
            ("uptown", -12, 16, -8, 19),        // the Square, Trade and Tryon, the one-way grid
            ("inner", 0, 13, 4, 16),            // Plaza Midwood: Central Ave, The Plaza, house streets
            ("suburb", -8, -16, -4, -13),       // SouthPark: Fairview, Sharon, arterials only
            ("interchange", -15, 21, -11, 24),  // I-77 at I-277 / Brookshire, the ramps, the streets under them
            ("rural", -34, 64, -30, 67),        // the north-west edge: I-485 and two-lane roads
        };

        public static void RunNetwork()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_network.txt"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
            var trims = CityMeshes.NodeTrims(map);
            NetworkCensus(map, trims);
            FinishTo("city_network.txt");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void NetworkCensus(CityMap map, CityMeshes.Trims trims)
        {
            Line($"NETWORK REVIEW {System.DateTime.Now:yyyy-MM-dd HH:mm}: {map.nodes.Length} nodes, {map.edges.Length} edges, graph hash {CityMap.GraphHashOf(map.edges):x8} (GHSH)");

            // ---- junction control as the game decides it
            var all = CitySignals.All(map, trims);
            int sigOsm = 0, sigWarrant = 0, allWay = 0, stop = 0, yieldOnly = 0;
            var sigCentres = new List<Vector2>();
            foreach (var j in all)
            {
                if (j.signal) { if (j.warrant) sigWarrant++; else sigOsm++; sigCentres.Add(j.centre); }
                else if (j.allWay) allWay++;
                else if (j.YieldOnly) yieldOnly++;
                else stop++;
            }
            Line("CONTROL (CitySignals): " + CitySignals.ControlReport());
            Line($"  junctions furnished {all.Count}: signals {sigOsm + sigWarrant} ({sigOsm} from OSM, {sigWarrant} by warrant), all-way STOP {allWay}, two-way STOP {stop}, yield only {yieldOnly}");

            // ---- the OSM signal nodes on the game's roads: is a signal junction there?
            string f = System.Environment.GetEnvironmentVariable("PSX_NET_OSMSIGNALS");
            if (!string.IsNullOrWhiteSpace(f) && File.Exists(f))
            {
                const float G = 96f;
                var grid = new Dictionary<long, List<int>>();
                long K(float x, float z) => ((long)Mathf.FloorToInt(x / G) << 32) | (uint)Mathf.FloorToInt(z / G);
                for (int i = 0; i < all.Count; i++)
                {
                    if (!all[i].signal) continue;
                    long k = K(all[i].centre.x, all[i].centre.y);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(i);
                }
                int n = 0, hit30 = 0, hit60 = 0; var used = new HashSet<int>(); var miss = new List<string>();
                var missNames = new Dictionary<string, int>();
                foreach (var row in File.ReadAllLines(f))
                {
                    var p = row.Split('\t');
                    if (p.Length < 4 || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                    n++;
                    float bd = float.MaxValue; int bi = -1;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue(K(x + dx * G, z + dz * G), out var l))
                                foreach (int i in l) { float d = Vector2.Distance(all[i].centre, new Vector2(x, z)); if (d < bd) { bd = d; bi = i; } }
                    if (bd <= 30f) hit30++;
                    if (bd <= 60f) { hit60++; used.Add(bi); continue; }
                    // what is there instead: the nearest junction node and the roads at it
                    string what = "mid-block";
                    int nn = -1; float nd = 45f;
                    var near = new HashSet<int>();
                    map.EdgeSegsInRect(new Vector2(x - 45f, z - 45f), new Vector2(x + 45f, z + 45f), near);
                    foreach (int pk in near)
                    {
                        var e = map.edges[pk >> 12];
                        foreach (int node in new[] { e.a, e.b })
                        {
                            float d = Vector2.Distance(map.nodes[node], new Vector2(x, z));
                            if (d < nd && map.nodeEdges[node].Count >= 3) { nd = d; nn = node; }
                        }
                    }
                    if (nn >= 0)
                    {
                        var names = new SortedSet<string>();
                        foreach (int ei in map.nodeEdges[nn]) { var e = map.edges[ei]; names.Add(string.IsNullOrEmpty(e.name) ? (e.link ? "(ramp)" : "(unnamed)") : e.name); }
                        what = $"n{nn} {string.Join(" x ", names)} at {nd:0} m";
                        string key = string.Join(" x ", names);
                        missNames[key] = missNames.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                    miss.Add($"{p[2]},{p[3]} {what}");
                }
                int unusedSig = 0; for (int i = 0; i < all.Count; i++) if (all[i].signal && !all[i].warrant && !used.Contains(i)) unusedSig++;
                Line($"  OSM SIGNALS on the game's roads: {n}; a signal junction of the game within 30 m: {hit30}, within 60 m: {hit60}; NONE within 60 m: {miss.Count} ({missNames.Count} distinct junctions with 3+ arms, the rest mid-block)");
                Line($"  game signal junctions (OSM kind) no OSM signal node maps to: {unusedSig}");
                var top = new List<KeyValuePair<string, int>>(missNames); top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 40; i++) sb.Append($" [{top[i].Key}]");
                Line("  junctions an OSM signal stands at with no game signal:" + sb);
                foreach (var m in miss) outLog.AppendLine("    NOSIGNAL " + m);
            }
            else Line("  (PSX_NET_OSMSIGNALS not given: the OSM signal comparison is skipped)");

            // ---- the freeway divider plan and the median cells
            Line("DIVIDER PLAN: " + CityMeshes.LastDividerReport);
            Line("DIVIDER GATE: " + CityMeshes.LastDividerGate);
            Line("MEDIAN CELLS: " + CityMeshes.MedianCellCensus(map));
            Line($"TURN POCKETS: {(CityMeshes.TurnPocketsOn ? "ON" : "OFF (PSX_CITY_TURNPOCKETS=1 draws them)")}");

            // ---- surface trunks: island or barrier, by name
            {
                var isl = new Dictionary<string, float>(); var bar = new Dictionary<string, float>();
                foreach (var e in map.edges)
                {
                    if (e.cls != 4 || !e.oneway || e.link || string.IsNullOrEmpty(e.name)) continue;
                    var d = CityMeshes.TrunkIsland(e) ? isl : bar;
                    d[e.name] = (d.TryGetValue(e.name, out float m) ? m : 0f) + e.length;
                }
                string L(Dictionary<string, float> d) { var l = new List<KeyValuePair<string, float>>(d); l.Sort((a, b) => b.Value.CompareTo(a.Value)); var sb = new StringBuilder(); foreach (var kv in l) if (kv.Value > 500f) sb.Append($" {kv.Key} {kv.Value / 1000f:0.0} km;"); return sb.ToString(); }
                Line("TRUNK MEDIANS (class 4 one-way carriageways): curbed island on" + L(isl));
                Line("  concrete barrier corridor on" + L(bar));
            }

            // ---- lane profiles the game draws, km
            {
                var km = new float[RoadProfiles.All.Length];
                foreach (var e in map.edges) km[Mathf.Clamp(e.profile, 0, km.Length - 1)] += e.length / 1000f;
                var sb = new StringBuilder();
                for (int i = 0; i < km.Length; i++) sb.Append($" {RoadProfiles.All[i].key} {km[i]:0.0};");
                Line("PROFILES drawn (km):" + sb);
            }
        }

        public static void RunRoadsSample()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_roads_sample.txt"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            routeOfEdge = RouteEdges(map);
            Line($"ROADS SAMPLE {System.DateTime.Now:yyyy-MM-dd HH:mm}: graph hash {CityMap.GraphHashOf(map.edges):x8} (GHSH); BUILD HEAD: {System.Environment.GetEnvironmentVariable("PSX_BUILD_HEAD") ?? "(unset)"}");
            NetworkCensus(map, trims);

            string pick = System.Environment.GetEnvironmentVariable("PSX_ROADS_SAMPLE");
            var want = new HashSet<string>();
            if (!string.IsNullOrWhiteSpace(pick)) foreach (var s in pick.Split(',', ';', ' ')) if (s.Trim().Length > 0) want.Add(s.Trim());
            string routesWas = System.Environment.GetEnvironmentVariable("PSX_DRIVE_ROUTES");
            string boxWas = System.Environment.GetEnvironmentVariable("PSX_DRIVE_BOX");
            try
            {
                foreach (var b in SampleBoxes)
                {
                    if (want.Count > 0 && !want.Contains(b.kind)) continue;
                    var tiles = new List<(int tx, int tz, string why)>();
                    for (int tz = b.z0; tz <= b.z1; tz++)
                        for (int tx = b.x0; tx <= b.x1; tx++) tiles.Add((tx, tz, b.kind));
                    float x0 = b.x0 * CityMeshes.TileSize, z0 = b.z0 * CityMeshes.TileSize, x1 = (b.x1 + 1) * CityMeshes.TileSize, z1 = (b.z1 + 1) * CityMeshes.TileSize;
                    // every road with a point on the box's tiles is counted
                    var on = new HashSet<int>();
                    foreach (var e in map.edges)
                        for (float s = 0f; s <= e.length + 7.99f; s += 8f)
                        {
                            var p = e.PointAt(Mathf.Min(s, e.length));
                            if (p.x >= x0 && p.x < x1 && p.y >= z0 && p.y < z1) { on.Add(e.index); break; }
                        }
                    float km = 0f; foreach (int ei in on) km += map.edges[ei].length / 1000f;
                    Line($"==== SAMPLE BOX {b.kind}: tiles x {b.x0}..{b.x1}, z {b.z0}..{b.z1} ({tiles.Count} tiles; world {x0:0},{z0:0} .. {x1:0},{z1:0}); {on.Count} edges ({km:0.0} km) ====");
                    var t0 = clock.Elapsed.TotalSeconds;
                    CityMeshes.DividerLapRecord = true;
                    CityMeshes.DividerLaps.Clear(); CityMeshes.DividerLappedRails.Clear();
                    CityMeshes.carryOnSkipped.Clear();
                    CityMeshes.sameWayDeckSides.Clear();
                    MedianLateralReset(map);
                    FallHolesReset();
                    DriveAudit(map, trims, buildings, tiles, on);
                    MedianLateralReport();
                    FallHolesReport();
                    Line($"  ({b.kind}: drive audit {clock.Elapsed.TotalSeconds - t0:0} s)");
                    t0 = clock.Elapsed.TotalSeconds;
                    System.Environment.SetEnvironmentVariable("PSX_DRIVE_ROUTES", "none");
                    System.Environment.SetEnvironmentVariable("PSX_DRIVE_BOX", string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", x0, z0, x1, z1));
                    InvisibleColliders(map, trims);
                    Line($"  ({b.kind}: invisible colliders {clock.Elapsed.TotalSeconds - t0:0} s)");
                }
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_DRIVE_ROUTES", routesWas);
                System.Environment.SetEnvironmentVariable("PSX_DRIVE_BOX", boxWas);
            }
            Line($"ROADS SAMPLE done in {clock.Elapsed.TotalSeconds:0} s");
            FinishTo("city_roads_sample.txt");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
