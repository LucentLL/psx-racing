using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CITY BUDGET (Charlotte refinement WP-01, plan section 2.4): what a
    /// streamed Charlotte costs, measured the same way at the same nine places
    /// before and after every package, so a package that spends draw calls,
    /// tile-build time or heap has to say so.
    ///
    /// At each site it builds the 5x5 ring the player drives in THROUGH
    /// CityWorld's own EnsureTile (the code the game runs, with the game's
    /// materials; CityWorld's clock splits each build into the mesh build,
    /// standing it up, the MeshCollider cook and the prop models), then from a
    /// 1.2 m eye on the nearest road looks four ways - along the road, right,
    /// back, left - with the game's 58 degree field and the city's 500 m far
    /// plane, and counts the draw proxy: submeshes of enabled renderers whose
    /// bounds meet the frustum (one draw each, before batching; cross-check it
    /// once with the Frame Debugger). Also the sun map's casters (one draw
    /// each, SunShadows.WouldCast) in the ring and within its 90 m box,
    /// vertices, triangles and colliders. Last, the map is parsed and solved
    /// again from the shipped bytes to time Parse and Solve and weigh the
    /// managed heap it holds.
    ///
    /// Numbers are EDITOR numbers: a phone's WebGL build is several times
    /// slower (plan critic C29 - WP-09's trigger is the FPS overlay's CITY
    /// line read on the phone, not these). Writes city_budget.txt at the
    /// project root. Run by CityAudit.Run; menu PSX Racing/City Budget Probe;
    /// headless -executeMethod PSXRacing.EditorTools.CityBudgetProbe.Run.
    /// </summary>
    public static class CityBudgetProbe
    {
        const float EyeM = 1.2f, FarM = 500f, FovDeg = 58f, Aspect = 16f / 9f;

        struct Site { public string name; public Vector2 at; public string how; public bool extra, bay; }

        static Vector2 LL(double lat, double lon)
        {
            // export_osm.mjs's frame: equirectangular about RG2's fixture centre
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        /// <summary>The nine sites of plan 2.4, fixed so every run measures
        /// the same ground.</summary>
        static List<Site> Sites(CityMap map)
        {
            var s = new List<Site>
            {
                new Site { name = "trade_tryon", at = new Vector2(-2314f, 4726f), how = "uptown, Trade & Tryon" },
                new Site { name = "providence", at = new Vector2(1184f, -1795f), how = "Providence Rd" },
                new Site { name = "i77_north", at = new Vector2(-2825f, 16870f), how = "I-77 north (the toll-drop kink)" },
                new Site { name = "beatties_ford", at = new Vector2(-8938f, 26444f), how = "Beatties Ford Rd, rural" },
            };
            // the Tryon Street Sprint's start line, walked along its chain
            foreach (var r in map.routes)
            {
                if (r.id != "tryon") continue;
                float left = r.startM;
                for (int k = 0; k < r.edges.Length; k++)
                {
                    var e = map.edges[r.edges[k]];
                    if (left <= e.length || k == r.edges.Length - 1)
                    {
                        float at = r.dirs[k] >= 0 ? Mathf.Min(left, e.length) : Mathf.Max(0f, e.length - left);
                        s.Add(new Site { name = "tryon_start", at = e.PointAt(at), how = "the Tryon Street Sprint's start line" });
                        break;
                    }
                    left -= e.length;
                }
            }
            s.Add(new Site { name = "dilworth", at = LL(35.19280, -80.83678), how = "Queens Rd W, Myers Park / Dilworth (Street View A8)" });
            s.Add(new Site { name = "plaza_midwood", at = LL(35.22019, -80.80899), how = "Central Ave, Plaza Midwood (Street View A11)" });
            s.Add(new Site { name = "i485_south", at = LL(35.06068, -80.76398), how = "I-485, the wooded south-east stretch (Street View A4)" });
            s.Add(new Site { name = "suburb_6km", at = LL(35.18300, -80.79000), how = "Randolph Rd, a prefab suburb 6.9 km out (outside the footprint core)" });
            return s;
        }

        [MenuItem("PSX Racing/City Budget Probe")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityBudget] no city data"); return; }
            Run(map);
        }

        /// <summary>Measure, write city_budget.txt, and return the summary
        /// lines (CityAudit prints them).</summary>
        public static List<string> Run(CityMap map)
        {
            var sb = new StringBuilder();
            var summary = new List<string>();
            void L(string s) { sb.AppendLine(s); Debug.Log("[CityBudget] " + s); }

            L("CITY BUDGET (editor; WP-01 instrument; plan section 2.4)");
            L($"eye {EyeM} m over the nearest road, fov {FovDeg}, aspect 16:9, far {FarM} m, four headings from the road's own; ring 5x5 tiles of {CityMeshes.TileSize} m via CityWorld.EnsureTile");
            L($"unity {Application.unityVersion}, {SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB");

            // EnsureCityTextures rewrites the city kit; the world reads it, as
            // the game does (WP-07) - no table is handed in.
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~cityBudget");
            var world = go.AddComponent<CityWorld>();
            world.ring = 2;
            var timings = new List<CityWorld.TileTiming>();
            System.Action<CityWorld.TileTiming> onBuilt = t => timings.Add(t);
            CityWorld.TileBuilt += onBuilt;

            var camGo = new GameObject("~cityBudgetEye");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = FovDeg; cam.aspect = Aspect; cam.nearClipPlane = 0.3f; cam.farClipPlane = FarM;

            var all = new List<CityWorld.TileTiming>();
            int worstDraw = 0; string worstDrawAt = "";
            var rows = new List<string>();
            var seats = new List<string>();
            var seatSummary = new StringBuilder();
            // The package's A/B: the same sites again as the city was before
            // it, so what it costs is measured on identical tiles in one run
            // (the machine is shared; only an A/B inside one run means
            // anything). WP-07's pass was the FULL prop prefabs; WP-08's the
            // city with NO TREES; WP-23's the city with NO SIGNS; WP-25's is
            // the tiles with NO CULVERTS AND NO CREEK BANKS (CityMeshes.HydroOff;
            // trees and signs on both ways, the ponds are data and in both).
            // Pass 0 is the game as it ships and is the only one in ALL; pass 1
            // is reported beside it.
            var fullRows = new List<string>();
            var fullAll = new List<CityWorld.TileTiming>();
            var treeAll = new List<CityWorld.TileTiming>();
            var treeP95BySite = new Dictionary<string, float>();
            var drawsByPass = new Dictionary<string, int[]>[] { new Dictionary<string, int[]>(), new Dictionary<string, int[]>() };
            var tilesByPass = new Dictionary<string, List<float>>[] { new Dictionary<string, List<float>>(), new Dictionary<string, List<float>>() };
            bool variantsWere = CityProps.UseCityVariants, treesWere = CityTrees.Enabled, signsWere = CitySigns.Enabled, hydroOffWas = CityMeshes.HydroOff;
            var treesDensityWas = CityTrees.DensityOverride;
            CityTrees.DensityOverride = 1f;
            var sites = Sites(map);
            // and the two restaurant lots (plan WP-07: a restaurant site
            // loses 300 or more): the first drive-thru and the first pizzeria
            // the placement table holds, seen from the road that fronts them
            // and from the chase camera of a car stopped in the order bay.
            // Not in ALL, so ALL stays the nine.
            foreach (byte kind in new[] { CityProps.Burger, CityProps.Pizzeria })
                foreach (var lot in world.FoodLots)
                    if (lot.kind == kind)
                    {
                        string n = kind == CityProps.Burger ? "burger" : "pizza";
                        sites.Add(new Site { name = n + "_lot", at = lot.pos, how = CityProps.FoodName(kind) + " lot from its road (restaurant prop, WP-07)", extra = true });
                        sites.Add(new Site { name = n + "_bay", at = lot.pos, how = CityProps.FoodName(kind) + ": the chase camera of a car stopped in the order bay", extra = true, bay = true });
                        break;
                    }
            // and the sign shots' two other roads (WP-23; I-77 north is one of the nine)
            sites.Add(new Site { name = "i277_uptown", at = LL(35.2195, -80.8500), how = "I-277, the uptown loop's north side (WP-23 sign shots)", extra = true });
            sites.Add(new Site { name = "south_blvd", at = LL(35.1930, -80.8680), how = "South Blvd, the commercial strip (WP-23 sign shots)", extra = true });
            // and two creek crossings (WP-25 water shots)
            sites.Add(new Site { name = "irwin_trade", at = LL(35.2345, -80.8560), how = "W Trade St over Irwin Creek (WP-25 water shots)", extra = true });
            sites.Add(new Site { name = "archdale_creek", at = LL(35.1500, -80.8500), how = "Archdale Dr over Little Sugar Creek (WP-25 water shots)", extra = true });
            try
            {
                // warm-up: the first tile pays the JIT and the static caches
                timings.Clear();
                world.EnsureTile(Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize), Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize));
                if (timings.Count > 0) L($"warm-up tile (not counted): {timings[0].totalMs:0.0} ms");
                world.DropAll();
                // and every prop both ways once, so neither pass of the A/B
                // below pays a first load, or the first cook of a piece's
                // MeshCollider, that the other then finds warm (a variant
                // carries the full prefab's own meshes and colliders)
                foreach (var kv in CityProps.Defs)
                    foreach (var pf in new[] { CityProps.Prefab(kv.Key), CityProps.CityPrefab(kv.Key) })
                        if (pf != null) Object.DestroyImmediate(Object.Instantiate(pf));

                // site by site, each built with trees and then without, back to
                // back, so machine load drifts across the pair as little as it
                // can (the WP-08 review: two passes a site list apart read one
                // site's p95 as 72 ms one way and 31 ms the other)
                // PSX_BUDGET_SITES=a,b (WP-09): only those sites, the shipping
                // pass only - a quick read of where one area's time goes.
                string onlySites = System.Environment.GetEnvironmentVariable("PSX_BUDGET_SITES");
                foreach (var site in sites)
                for (int pass = 0; pass < 2; pass++)
                {
                    if (!string.IsNullOrEmpty(onlySites) && (pass != 0 || System.Array.IndexOf(onlySites.Split(','), site.name) < 0)) continue;
                    CityProps.UseCityVariants = true;
                    CityTrees.Enabled = true;
                    CitySigns.Enabled = true;
                    CityMeshes.HydroOff = pass != 0;
                    if (pass == 0 && !site.extra)
                    {
                    // THE SPAWN SEAT here, by CityMode.SeatOnStreet's rule:
                    // the nearest non-link road within 120 m, else any road
                    // within 600 m, at its solved height. A format or datum
                    // change must leave these to the tenth of a millimetre
                    // (WP-02's acceptance); a ground change moves them on
                    // purpose, and says by how much.
                    if (map.NearestRoadPoint(site.at, 120f, true, out int sei, out float sat, out _) ||
                        map.NearestRoadPoint(site.at, 600f, false, out sei, out sat, out _))
                    {
                        var se = map.edges[sei];
                        var sq = se.PointAt(sat);
                        seats.Add($"spawn {site.name,-14} edge {sei} way {se.wayId} at {sat:0.000} m  seat ({sq.x:0.000},{sq.y:0.000})  road y {se.YAt(sat):0.0000}  ground y at site {CityElevation.BaseY(site.at.x, site.at.y):0.0000}");
                        seatSummary.Append(seatSummary.Length == 0 ? "" : ", ").Append($"{site.name} {se.YAt(sat):0.000}");
                    }
                    else { seats.Add($"spawn {site.name,-14} no road within 600 m"); seatSummary.Append(seatSummary.Length == 0 ? "" : ", ").Append($"{site.name} none"); }
                    }

                    timings.Clear();
                    CityMeshes.ResetPhaseClock();
                    world.EnsureRing(new Vector3(site.at.x, 0f, site.at.y), 2);
                    if (pass == 0)
                    {
                        var ph = new StringBuilder($"phases {site.name,-14} (sum over the ring / longest in one tile, ms):");
                        for (int i = 0; i < CityMeshes.PhaseNames.Length; i++)
                            ph.Append($"  {CityMeshes.PhaseNames[i]} {CityMeshes.PhaseMs[i]:0}/{CityMeshes.PhaseMaxMs[i]:0}");
                        L(ph.ToString());
                    }
                    // the tile builds, and (WP-08) the tree frames: a tile's
                    // trees plant on a frame of their own after its build
                    var treeFrames = timings.FindAll(t => t.treeFrame);
                    timings.RemoveAll(t => t.treeFrame);
                    if (!site.extra) (pass == 0 ? all : fullAll).AddRange(timings);
                    if (!site.extra && pass == 0) treeAll.AddRange(treeFrames);
                    int tableTrunks = world.Trunks != null ? world.Trunks.TableTrunks : 0;

                    // the eye: on the nearest road, looking along it
                    Vector3 eye; Vector2 fwd;
                    if (map.NearestRoadPoint(site.at, 400f, false, out int ei, out float s, out float dist))
                    {
                        var e = map.edges[ei];
                        var p = e.PointAt(s);
                        eye = new Vector3(p.x, e.YAt(s) + EyeM, p.y);
                        fwd = e.TangentAt(s);
                    }
                    else { eye = new Vector3(site.at.x, CityElevation.BaseY(site.at.x, site.at.y) + EyeM, site.at.y); fwd = Vector2.up; }
                    string bayNote = "";
                    // the player's car: under a road eye; in the bay, stopped
                    // where it orders (CityPropBaker.BayStop), the chase rig's
                    // default 5.4 m back and 1.8 m up, facing along the building
                    Vector3 car = eye - Vector3.up * EyeM;
                    if (site.bay)
                    {
                        DriveThru bay = null; float bayD = float.MaxValue;
                        foreach (var d in go.GetComponentsInChildren<DriveThru>(false))
                        {
                            float dd = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), site.at);
                            if (dd < bayD) { bayD = dd; bay = d; }
                        }
                        if (bay != null && bayD < 60f)
                        {
                            car = CityPropBaker.BayStop(bay, bay.GetComponentInParent<CityPropInterior>(), out var along);
                            eye = car - along * 5.4f + Vector3.up * 1.8f;
                            fwd = new Vector2(along.x, along.z);
                        }
                        else bayNote = " (NO ORDER BAY FOUND - the lot's own road eye)";
                    }

                    // THE ROOM SWITCH at this eye and car, as the game runs it
                    // (CityPropInterior: drawn from inside the hull, or through
                    // a door that swings open for the car)
                    int roomsOn = 0; float roomNear = float.MaxValue;
                    foreach (var sw in go.GetComponentsInChildren<CityPropInterior>(false))
                    {
                        if (sw.Apply(eye, car)) roomsOn++;
                        roomNear = Mathf.Min(roomNear, Mathf.Min(sw.DistanceTo(eye), sw.DistanceTo(car + Vector3.up * 0.7f)));
                    }
                    string roomNote = roomNear < float.MaxValue
                        ? $"; nearest restaurant room {roomNear:0.0} m from the eye or car, {roomsOn} drawn by the switch"
                        : "";

                    var rends = go.GetComponentsInChildren<MeshRenderer>(false);
                    int[] draws = new int[4];
                    for (int h = 0; h < 4; h++)
                    {
                        var d = Quaternion.Euler(0f, 90f * h, 0f) * new Vector3(fwd.x, 0f, fwd.y);
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d, Vector3.up));
                        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                        foreach (var r in rends)
                        {
                            if (!r.enabled) continue;
                            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                            draws[h] += DrawsOf(r);
                        }
                    }
                    int verts = 0; long tris = 0;
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(false))
                    {
                        var m = mf.sharedMesh; if (m == null) continue;
                        verts += m.vertexCount;
                        for (int k = 0; k < m.subMeshCount; k++) tris += m.GetIndexCount(k) / 3;
                    }
                    int casters = 0, castersNear = 0;
                    foreach (var r in rends)
                    {
                        if (!r.enabled || !SunShadows.WouldCast(r)) continue;
                        casters++;
                        var b = r.bounds;
                        var dxz = new Vector2(Mathf.Max(0f, Mathf.Abs(b.center.x - eye.x) - b.extents.x), Mathf.Max(0f, Mathf.Abs(b.center.z - eye.z) - b.extents.z));
                        if (dxz.x <= SunShadows.HalfExtentM && dxz.y <= SunShadows.HalfExtentM) castersNear++;
                    }
                    int colliders = go.GetComponentsInChildren<Collider>(false).Length;
                    var tot = new List<float>(); var bld = new List<float>(); var cook = new List<float>();
                    int props = 0, treeN = 0, signN = 0; float propsMs = 0f; var treeMs = new List<float>(); var plantMs = new List<float>(); var signMs = new List<float>();
                    foreach (var t in timings) { tot.Add(t.totalMs); bld.Add(t.buildMs); cook.Add(t.cookMs); props += t.props; propsMs += t.propsMs; }
                    foreach (var t in treeFrames) { treeN += t.trees; signN += t.signs; treeMs.Add(t.treesMs); plantMs.Add(t.treePlantMs); signMs.Add(t.signsMs); }
                    int treeViews = 0, signViews = 0;
                    foreach (var r in rends) { if (r.enabled && r.gameObject.name == "Trees") treeViews++; if (r.enabled && r.gameObject.name == "Signs") signViews++; }
                    int dmax = Mathf.Max(Mathf.Max(draws[0], draws[1]), Mathf.Max(draws[2], draws[3]));
                    if (pass == 0 && !site.extra && dmax > worstDraw) { worstDraw = dmax; worstDrawAt = site.name; }
                    drawsByPass[pass][site.name] = draws;
                    if (pass == 0) treeP95BySite[site.name] = P(treeMs, 95);
                    tilesByPass[pass][site.name] = tot;
                    (pass == 0 ? rows : fullRows).Add($"{site.name,-14} {timings.Count,3} tiles  total p50 {P(tot, 50),5:0.0} p95 {P(tot, 95),5:0.0} max {P(tot, 100),5:0.0} ms  build p95 {P(bld, 95),5:0.0}  cook p95 {P(cook, 95),5:0.0}  " +
                             $"draws {draws[0],4}/{draws[1],4}/{draws[2],4}/{draws[3],4}  casters {casters,4} ({castersNear} in the 90 m box)  verts {verts / 1000,5}k  tris {tris / 1000,5}k  colliders {colliders,4}  props {props,3} ({propsMs:0.0} ms to stand up)  " +
                             $"trees {treeN,5} on {treeViews} tiles, TREE FRAME p50 {P(treeMs, 50):0.0} p95 {P(treeMs, 95):0.0} max {P(treeMs, 100):0.0} ms (planting p95 {P(plantMs, 95):0.0}), {tableTrunks} solid trunks in the table; " +
                             $"signs {signN,3} on {signViews} tiles (placing p95 {P(signMs, 95):0.0} ms of the tree frame)");
                    if (pass == 0) L($"{site.name}: {site.how}; eye ({eye.x:0},{eye.y:0.0},{eye.z:0}){(!site.bay && (dist > 30f || site.extra) ? $", {dist:0} m from the site" : "")}{bayNote}{roomNote}");
                    world.DropAll();
                }
            }
            finally
            {
                CityProps.UseCityVariants = variantsWere;
                CityTrees.Enabled = treesWere;
                CitySigns.Enabled = signsWere;
                CityMeshes.HydroOff = hydroOffWas;
                CityTrees.DensityOverride = treesDensityWas;
                CityWorld.TileBuilt -= onBuilt;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(go);
            }

            L("");
            L("site           tiles  tile build (whole EnsureTile), mesh build, collider cook        draws ahead/right/back/left   sun-map casters   ring totals");
            foreach (var r in rows) L(r);
            var totAll = new List<float>(); var cookAll = new List<float>(); var bldAll = new List<float>();
            foreach (var t in all) { totAll.Add(t.totalMs); cookAll.Add(t.cookMs); bldAll.Add(t.buildMs); }
            L($"ALL {all.Count} tiles: total p50 {P(totAll, 50):0.0} p95 {P(totAll, 95):0.0} max {P(totAll, 100):0.0} ms; mesh build p95 {P(bldAll, 95):0.0}; cook p95 {P(cookAll, 95):0.0}; worst view {worstDraw} draws at {worstDrawAt}");
            summary.Add($"budget: {all.Count} tiles at 9 sites, tile build p50 {P(totAll, 50):0.0} / p95 {P(totAll, 95):0.0} / max {P(totAll, 100):0.0} ms (editor), worst view {worstDraw} draws ({worstDrawAt})");
            // plan: this package decides WP-09's place (in the editor; the phone reading is C29's trigger)
            L($"WP-09 trigger (editor): tile p95 {(P(totAll, 95) > 8f ? "OVER" : "under")} 8 ms, worst view {(worstDraw > 300 ? "OVER" : "under")} 300 draws");

            // ---- WP-25: the same sites with no culverts and no banks --------
            L("");
            L("WP-25 A/B - the same sites and tiles with NO CULVERTS AND NO CREEK BANKS (the tiles before WP-25; trees, signs and the ponds both ways), each site built both ways back to back:");
            foreach (var r in fullRows) L("nohydro " + r);
            var fullTot = new List<float>();
            foreach (var t in fullAll) fullTot.Add(t.totalMs);
            L($"nohydro ALL {fullAll.Count} tiles: total p50 {P(fullTot, 50):0.0} p95 {P(fullTot, 95):0.0} max {P(fullTot, 100):0.0} ms");
            // the trees' own cost, timed directly: a frame of their own per tile
            var treeTot = new List<float>(); var treePlant = new List<float>();
            foreach (var t in treeAll) { treeTot.Add(t.totalMs); treePlant.Add(t.treePlantMs); }
            L($"TREE FRAMES ALL {treeAll.Count} tiles: p50 {P(treeTot, 50):0.0} p95 {P(treeTot, 95):0.0} max {P(treeTot, 100):0.0} ms (planting p95 {P(treePlant, 95):0.0}); " +
              $"a tile's trees never share a frame with a tile build (CityWorld.PlantTrees), and stand no collider there (the trunk table stands them round the cars)");
            L("draws the CULVERTS AND BANKS add, per heading (ahead/right/back/left), and the most in one view (a tile's banks one draw, its headwalls the barriers' draw and its pipes the lamp posts'):");
            int mostAdded = 0; string mostAt = "";
            foreach (var site in sites)
            {
                if (!drawsByPass[0].TryGetValue(site.name, out var dv) || !drawsByPass[1].TryGetValue(site.name, out var df)) continue;
                int best = 0;
                var parts = new string[4];
                for (int h = 0; h < 4; h++) { parts[h] = (dv[h] - df[h]).ToString(); best = Mathf.Max(best, dv[h] - df[h]); }
                float p95v = P(tilesByPass[0][site.name], 95), p95f = P(tilesByPass[1][site.name], 95);
                treeP95BySite.TryGetValue(site.name, out float tf95);
                L($"  {site.name,-14} added {string.Join("/", parts)}  most {best}  (tile build p95 {p95f:0.0} without, {p95v:0.0} with; the tree frame p95 {tf95:0.0} ms)");
                if (!site.extra && best > mostAdded) { mostAdded = best; mostAt = site.name; }
            }
            summary.Add($"budget: WP-25 culverts and banks - at most +{mostAdded} draws in one view ({mostAt}); tile build p95 {P(fullTot, 95):0.0} (without) / {P(totAll, 95):0.0} (with) ms on the same tiles; " +
                        $"tree frames p95 {P(treeTot, 95):0.0} / max {P(treeTot, 100):0.0} ms");

            L("");
            L($"spawn seats (CityMode.SeatOnStreet's rule; datum {CityElevation.DatumASL:0.000} m ASL, graph hash {map.graphHash:x8})");
            foreach (var s in seats) L(s);
            summary.Add("budget: spawn road y (m above the datum) " + seatSummary);

            // ---- Parse + Solve and the heap they hold ----------------------
            var cityTa = Resources.Load<TextAsset>("charlotte_city");
            var bldTa = Resources.Load<TextAsset>("charlotte_bld");
            var demTa = Resources.Load<TextAsset>("charlotte_dem");
            if (cityTa != null)
            {
                byte[] a = cityTa.bytes, b = bldTa != null ? bldTa.bytes : null;
                // what the grid holds resident (WP-13: the v3 blob and its block
                // cache; before, the whole ushort[]), not the file's length
                long demBytes = demTa != null ? CityElevation.DemResidentBytes : 0;
                System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
                long before = System.GC.GetTotalMemory(true);
                var again = CityMap.Parse(a, b);
                long after = System.GC.GetTotalMemory(true);
                float heldMb = (after - before) / (1024f * 1024f);
                L("");
                L($"solve phases: {CityElevation.LastSolvePhases}");
                L($"parse {CityMap.LastParseMs:0} ms, solve {CityMap.LastSolveMs:0} ms (editor); the parsed + solved map holds {heldMb:0.0} MB of managed heap " +
                  $"({again.edges.Length} edges, {again.footprints.Length} footprints); the DEM holds {demBytes / (1024f * 1024f):0.0} MB more (PDEM v{CityElevation.DemVersion}: {(CityElevation.DemVersion >= 3 ? "the compressed blocks and " + CityElevation.BlockCacheSlots + " decoded" : "the whole ushort[]")}, {CityElevation.BlockDecodes} block decodes so far); " +
                  $"data bytes: city {a.Length / 1024} KB, bld {(b != null ? b.Length / 1024 : 0)} KB, dem {demBytes / 1024} KB");
                summary.Add($"budget: parse {CityMap.LastParseMs:0} ms + solve {CityMap.LastSolveMs:0} ms, map heap {heldMb:0.0} MB (+{demBytes / (1024f * 1024f):0.0} MB DEM)");
                System.GC.KeepAlive(again);
                Resources.UnloadAsset(cityTa);
                if (bldTa != null) Resources.UnloadAsset(bldTa);
                if (demTa != null) Resources.UnloadAsset(demTa);
            }

            // ---- WP-09: the time-sliced build ------------------------------
            // Every tile of the Uptown rings built twice - in one go, and as a
            // TileJob one step a call - and the two compared mesh by mesh; and
            // the steps timed, since the longest step is the floor of the
            // worst frame a sliced build can cost.
            if (CityWorld.SharedTrims != null)
            {
                L("");
                L($"WP-09 time-sliced build: each tile built as before WP-09 (one go, no sectioning ahead) and as streaming builds it (sliced, one step a call), compared; budget {CityWorld.SliceBudgetMs} ms a frame ({CityWorld.UrgentSliceBudgetMs} ms next to a car)");
                var stepsAll = new List<float>();
                int tilesCmp = 0, mismatches = 0;
                string onlySites2 = System.Environment.GetEnvironmentVariable("PSX_BUDGET_SITES");
                foreach (var site in sites)
                {
                    bool uptown = site.name == "trade_tryon" || site.name == "i277_uptown" || site.name == "irwin_trade";
                    if (!string.IsNullOrEmpty(onlySites2) ? System.Array.IndexOf(onlySites2.Split(','), site.name) < 0 : !uptown) continue;
                    int stx = Mathf.FloorToInt(site.at.x / CityMeshes.TileSize), stz = Mathf.FloorToInt(site.at.y / CityMeshes.TileSize);
                    var steps = new List<float>();
                    var bigSteps = new List<(int phase, int item, int part, float ms, float missMs)>();
                    float worstSlice = 0f; int framesMax = 0, framesSum = 0, n = 0;
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            // the baseline is the build as it was before WP-09: no
                            // sectioning ahead (the steps alone change nothing)
                            CityMeshes.WarmSections = false;
                            var one = CityMeshes.Build(map, CityWorld.SharedTrims, CityWorld.SharedBuildings, stx + dx, stz + dz);
                            CityMeshes.WarmSections = true;
                            string a = TileSig(one);
                            KillTile(one);
                            // one step a call: every step's own time
                            var job = CityMeshes.Begin(map, CityWorld.SharedTrims, CityWorld.SharedBuildings, stx + dx, stz + dz);
                            job.StepLog = steps;
                            job.BigSteps = bigSteps;
                            while (!job.Step(0.0)) { }
                            string b = TileSig(job.Result);
                            KillTile(job.Result);
                            tilesCmp++;
                            if (a != b) { mismatches++; L($"  MISMATCH tile {stx + dx},{stz + dz}: one go {a} / sliced {b}"); }
                            // and as streaming runs it: the ordinary budget a frame
                            var job2 = CityMeshes.Begin(map, CityWorld.SharedTrims, CityWorld.SharedBuildings, stx + dx, stz + dz);
                            while (!job2.Step(CityWorld.SliceBudgetMs)) { }
                            KillTile(job2.Result);
                            worstSlice = Mathf.Max(worstSlice, (float)job2.MaxSliceMs);
                            framesMax = Mathf.Max(framesMax, job2.Slices); framesSum += job2.Slices; n++;
                        }
                    stepsAll.AddRange(steps);
                    steps.Sort();
                    bigSteps.Sort((x, y) => y.ms.CompareTo(x.ms));
                    string[] roadParts = { "sections", "side flags", "spans", "lamps" };
                    for (int i = 0; i < Mathf.Min(12, bigSteps.Count); i++)
                    {
                        var bs = bigSteps[i];
                        string what = bs.part == 4 ? "sectioning ahead" : bs.part == 5 ? "a neighbour's sections" : bs.part == 10 ? "side flags: setup + structure ends" : bs.part == 11 ? "side flags: spans"
                                    : bs.part == 12 ? "side flags: runs + flares" : bs.part == 20 ? "branch: chains" : bs.part == 21 ? "branch: stations"
                                    : bs.part == 22 ? "branch: clips" : bs.phase == 2 ? roadParts[Mathf.Clamp(bs.part, 0, 3)] : "";
                        string edge = (bs.phase == 1 || bs.phase == 2) && bs.item >= 0 && bs.item < map.edges.Length
                            ? $" (edge {bs.item}, way {map.edges[bs.item].wayId}, {map.edges[bs.item].length:0} m)" : $" (item {bs.item})";
                        L($"    big step {bs.ms,6:0.0} ms (sectioning unseen edges {bs.missMs:0.0})  {CityMeshes.PhaseNames[bs.phase]} {what}{edge}");
                    }
                    L($"  {site.name,-14} {n} tiles, {steps.Count} steps: step p50 {SP(steps, 50):0.00} p95 {SP(steps, 95):0.00} p99 {SP(steps, 99):0.00} max {SP(steps, 100):0.0} ms; " +
                      $"at {CityWorld.SliceBudgetMs} ms a frame a tile takes {framesSum / Mathf.Max(1, n)} frames (most {framesMax}), its worst frame {worstSlice:0.0} ms");
                }
                stepsAll.Sort();
                string verdict = mismatches == 0 ? "identical" : mismatches + " MISMATCH(ES)";
                L($"WP-09: {tilesCmp} tiles {verdict}; step max {SP(stepsAll, 100):0.0} ms, p99 {SP(stepsAll, 99):0.00} ms");
                summary.Add($"budget: WP-09 sliced build - {tilesCmp} tiles {verdict} to the one-go build; longest step {SP(stepsAll, 100):0.0} ms (p99 {SP(stepsAll, 99):0.00})");
            }

            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_budget.txt"), sb.ToString());
            return summary;
        }

        static float SP(List<float> sorted, float pct)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(pct / 100f * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[i];
        }

        /// <summary>A tile's meshes, bit for bit: vertex and index counts and a
        /// hash of every position, mesh by mesh, plus its lamps.</summary>
        static string TileSig(CityMeshes.TileMeshes tm)
        {
            var sb = new StringBuilder();
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings })
            {
                if (m == null) { sb.Append("-;"); continue; }
                var v = m.vertices;
                long h = 17;
                foreach (var p in v)
                    h = ((h * 31 + System.BitConverter.SingleToInt32Bits(p.x)) * 31 + System.BitConverter.SingleToInt32Bits(p.y)) * 31 + System.BitConverter.SingleToInt32Bits(p.z);
                sb.Append(v.Length).Append(':').Append(m.triangles.Length).Append(':').Append(h).Append(';');
            }
            sb.Append(tm.lamps.Count);
            return sb.ToString();
        }

        static void KillTile(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings })
                if (m != null) Object.DestroyImmediate(m);
        }

        /// <summary>Draw calls one renderer costs before batching: one per
        /// material slot (a slot past the last submesh draws that submesh
        /// again; a submesh with no slot is not drawn).</summary>
        static int DrawsOf(MeshRenderer r) => Mathf.Max(1, r.sharedMaterials.Length);

        static float P(List<float> v, float pct)
        {
            if (v.Count == 0) return 0f;
            var a = new List<float>(v); a.Sort();
            float i = pct / 100f * (a.Count - 1);
            int lo = Mathf.FloorToInt(i), hi = Mathf.CeilToInt(i);
            return Mathf.Lerp(a[lo], a[hi], i - lo);
        }
    }
}
