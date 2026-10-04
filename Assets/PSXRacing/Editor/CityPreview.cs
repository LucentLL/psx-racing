using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photographs the streamed city without play mode — the runtime-built
    /// world is invisible until it is rendered, and every failure mode here
    /// (a floating road, a black facade, a deck with no piers, a gore that
    /// missed its mainline) is visual and silent. Builds a ring of real tiles
    /// at a handful of probe spots, shoots each top-down and at street level,
    /// and tears it all down.
    ///
    /// Menu: PSX Racing/Preview Charlotte. Headless: -executeMethod
    /// PSXRacing.EditorTools.CityPreview.Run — PNGs land in Screenshots/City.
    ///
    /// PSX_PREVIEW_SPOTS (roads pass A1, 2026-10-02): a comma list of shot
    /// names or GROUPS ("w5th_owner,merge_i85_e3031_far", "merges", "all";
    /// a trailing * matches a prefix). Unset: the probes and views as before
    /// plus the W 5th group. The roads pass's NAMED VIEWS
    /// (<see cref="NamedViews"/>: w5th, twin, profiles, merges, junctions,
    /// paint, lateral) are shot only when named, so a package shoots its two
    /// to four views without the whole set; preview_spots.txt says where
    /// each camera stood.
    /// </summary>
    public static class CityPreview
    {
        [MenuItem("PSX Racing/Preview Charlotte")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityPreview] no city data"); return; }

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Screenshots", "City");
            Directory.CreateDirectory(dir);
            var want = SpotFilter.FromEnv();

            var probes = new List<(string name, Vector2 at, int ring)>
            {
                ("uptown", map.uptown, 1),
            };
            // a freeway-over-freeway viaduct, a street over a trenched
            // freeway, a water bridge, a ramp gore, I-485
            for (int i = 0; i < map.crossings.Length; i++)
            {
                var c = map.crossings[i];
                var o = map.edges[c.over]; var u = map.edges[c.under];
                if (o.cls >= 5 && !o.link && u.cls >= 5 && !u.link) { probes.Add(("overpass", c.at, 1)); break; }
            }
            for (int i = 0; i < map.crossings.Length; i++)
                if (CityElevation.TrenchedCrossings != null && CityElevation.TrenchedCrossings[i])
                {
                    var c = map.crossings[i];
                    if (Vector2.Distance(c.at, map.uptown) < 1500f) { probes.Add(("trench", c.at, 1)); break; }
                }
            if (map.wspans.Length > 0)
            {
                var ws = map.wspans[map.wspans.Length / 2];
                var e = map.edges[ws.edge];
                probes.Add(("bridge", e.PointAt((ws.s0 + ws.s1) * 0.5f), 1));
            }
            foreach (var e in map.edges)
                if (e.link && e.cls >= 5 && Vector2.Distance(map.nodes[e.b], map.uptown) > 3000f)
                { probes.Add(("gore", map.nodes[e.b], 1)); break; }
            foreach (var e in map.edges)
                if (e.name == "I-485" && !e.link && e.length > 300f) { probes.Add(("i485", e.PointAt(e.length * 0.5f), 1)); break; }

            PSXRacingBuilder.EnsureCityTextures();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);

            // The prop lots are the thing most likely to be silently wrong (a
            // floating house, a restaurant in a junction): photograph a
            // drive-thru, the housiest prefab suburb, a real-footprint
            // neighbourhood and a filled block on every run.
            Vector2? burgerAt = null, pizzaAt = null, suburbAt = null;
            int bestHouses = 0;
            foreach (var kv in buildings)
            {
                int houses = 0;
                Vector2 first = Vector2.zero;
                foreach (var b in kv.Value)
                {
                    if (b.kind == CityProps.Burger && burgerAt == null) burgerAt = b.pos;
                    if (b.kind == CityProps.Pizzeria && pizzaAt == null) pizzaAt = b.pos;
                    if (b.kind == CityProps.House) { houses++; first = b.pos; }
                }
                if (houses > bestHouses) { bestHouses = houses; suburbAt = first; }
            }
            if (suburbAt.HasValue) probes.Add(("suburb", suburbAt.Value, 1));
            // a footprint neighbourhood: the tile with the most gabled footprints
            int bestGables = 0; Vector2 gableAt = map.uptown;
            var gableCount = new Dictionary<long, (int n, Vector2 at)>();
            foreach (var f in map.footprints)
            {
                if (!f.gable) continue;
                long k = ((long)Mathf.FloorToInt(f.centre.x / 256f) << 24) ^ (Mathf.FloorToInt(f.centre.y / 256f) & 0xFFFFFF);
                gableCount.TryGetValue(k, out var cur);
                gableCount[k] = (cur.n + 1, f.centre);
            }
            foreach (var kv in gableCount) if (kv.Value.n > bestGables) { bestGables = kv.Value.n; gableAt = kv.Value.at; }
            if (bestGables > 0) probes.Add(("footprints", gableAt, 1));
            // a filled block outside the footprint data
            foreach (var e in map.edges)
            {
                if (e.cls != 2 || e.link) continue;
                var p = e.PointAt(e.length * 0.5f);
                float d = Vector2.Distance(p, map.uptown);
                if (d < 9000f || d > 12000f || map.footprintBounds.Contains(p)) continue;
                probes.Add(("interior", p, 1)); break;
            }
            if (burgerAt.HasValue) probes.Add(("burger", burgerAt.Value, 1));
            if (pizzaAt.HasValue) probes.Add(("pizzeria", pizzaAt.Value, 1));
            // the skyline: uptown from the south, with a bigger ring so the
            // towers stand in a city and not on an island
            probes.Add(("skyline", map.uptown, 2));

            // The trouble spots (2026-09-12): the West 5th Street bridge over
            // I-77 that the owner photographed with a ledge at its mouth, and
            // every freeway-to-freeway interchange — top-down at a wider
            // field, then at windscreen height along the mainline both ways,
            // then from thirty metres up looking down the road.
            var views = new List<(string name, Vector2 at, int ring, CityMap.Edge along, float s)>();
            foreach (var e in map.edges)
                if (e.bridge && e.name == "West 5th Street")
                { views.Add(("w5th", e.PointAt(e.length * 0.5f), 1, e, e.length * 0.5f)); break; }
            foreach (var (a, b) in new[] { ("I-277", "I-77"), ("I-277", "Independence"), ("I-77", "I-485"), ("I-85", "I-485") })
            {
                int k = 0;
                foreach (var ic in CityAudit.Interchanges(map, a, b))
                    views.Add(($"ic_{Tag(a)}_{Tag(b)}_{k++}", ic.at, 2, ic.mainline, ic.s));
            }
            // The 2026-09-12 pass: I-77 north of uptown, where the express
            // lanes were (the paint must run straight, and the outside
            // shoulder must be open), and a creek bridge the DEM cannot see
            // (the deck must stand over a dip, not in the grass).
            if (CityAudit.FindI77North(map, out var i77n))
                views.Add(("i77n", i77n.PointAt(i77n.length * 0.5f), 1, i77n, i77n.length * 0.5f));
            if (CityAudit.FindCreekBridge(map, out var creek))
                views.Add(("creek", creek.PointAt(creek.length * 0.5f), 1, creek, creek.length * 0.5f));

            // PSX/Lit reads global fog + snap; give it a daylight look
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var world = new GameObject("~CityPreviewWorld");
            var roots = new List<GameObject> { world };
            var spotLog = new System.Text.StringBuilder("shot\tgroup\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\tortho\tfov\twhat\n");
            int named = 0, shotProbes = 0, shotViews = 0;

            try
            {
                foreach (var (name, at, ring) in probes)
                {
                    if (!want.Takes(name, "probes", true)) continue;
                    shotProbes++;
                    var tiles = BuildRing(map, trims, buildings, world, at, ring, out var stats);
                    roots.AddRange(tiles);
                    Debug.Log($"[CityPreview] {name}: {stats}");

                    float midY = map.NearestRoadPoint(at, 300f, false, out int ei, out float s, out _)
                        ? map.edges[ei].YAt(s) : CityElevation.BaseY(at.x, at.y);

                    if (name == "skyline")
                    {
                        Shoot(dir, "skyline", new Vector3(at.x - 120f, midY + 28f, at.y - 620f),
                              Quaternion.LookRotation(new Vector3(0.18f, 0.04f, 1f)), ortho: 0f, far: 2500f);
                    }
                    else
                    {
                        Shoot(dir, name + "_top",
                            new Vector3(at.x, midY + 260f, at.y),
                            Quaternion.Euler(90f, 0f, 0f), ortho: 200f);
                        Shoot(dir, name + "_street",
                            new Vector3(at.x - 40f, midY + 6f, at.y - 40f),
                            Quaternion.LookRotation(new Vector3(1f, -0.08f, 1f)), ortho: 0f);
                    }

                    foreach (var t in tiles) Object.DestroyImmediate(t);
                    roots.RemoveAll(r => r == null);
                }

                foreach (var (name, at, ring, along, s) in views)
                {
                    if (!want.Takes(name, "views", true)) continue;
                    shotViews++;
                    var tiles = BuildRing(map, trims, buildings, world, at, ring, out var stats);
                    roots.AddRange(tiles);
                    Debug.Log($"[CityPreview] {name}: {stats}");
                    float midY = along.YAt(s);
                    Shoot(dir, name + "_top", new Vector3(at.x, midY + 300f, at.y),
                          Quaternion.Euler(90f, 0f, 0f), ortho: ring >= 2 ? 330f : 150f, w: 1280, h: 720);
                    for (int side = 0; side < 2; side++)
                    {
                        float dirS = side == 0 ? 1f : -1f;
                        float s0 = Mathf.Clamp(s - dirS * 70f, 0f, along.length);
                        // on the lanes' centre (the line model: a ribbon can sit off its OSM line)
                        var p0 = LineModel.LanePoint(along, s0);
                        var t0 = along.TangentAt(s0) * dirS;
                        var fwd = new Vector3(t0.x, 0f, t0.y);
                        var eye = new Vector3(p0.x, along.YAt(s0) + 1.5f, p0.y);
                        Shoot(dir, name + (side == 0 ? "_ahead" : "_back"), eye,
                              Quaternion.LookRotation(fwd + Vector3.down * 0.04f), ortho: 0f, w: 1280, h: 720);
                        if (side == 0)
                            Shoot(dir, name + "_high", eye - fwd * 60f + Vector3.up * 32f,
                                  Quaternion.LookRotation(fwd + Vector3.down * 0.34f), ortho: 0f, w: 1280, h: 720);
                    }
                    foreach (var t in tiles) Object.DestroyImmediate(t);
                    roots.RemoveAll(r => r == null);
                }

                // the roads pass's named views (A1): shot only when asked for (W 5th by default)
                foreach (var v in NamedViews)
                {
                    if (!want.Takes(v.name, v.group, v.group == "w5th")) continue;
                    if (!ShootNamed(map, trims, buildings, world, roots, dir, v, spotLog)) Debug.LogWarning($"[CityPreview] {v.name}: no road to stand on near ({v.at.x:0},{v.at.y:0})");
                    else named++;
                }
                File.WriteAllText(Path.Combine(dir, "preview_spots.txt"), spotLog.ToString());
                Debug.Log($"[CityPreview] wrote shots for {shotProbes} of {probes.Count} probes, {shotViews} of {views.Count} views and {named} named views to {dir}{(want.All ? "" : " (PSX_PREVIEW_SPOTS=" + want.Raw + ")")}");
            }
            finally
            {
                foreach (var r in roots) if (r != null) Object.DestroyImmediate(r);
            }
        }

        // =====================================================================
        //  The roads pass's named views (plan A1, 2026-10-02)
        // =====================================================================

        /// <summary>PSX_PREVIEW_SPOTS: which shots to take.</summary>
        sealed class SpotFilter
        {
            public string Raw = ""; public bool All = true; readonly List<string> tokens = new List<string>();
            public static SpotFilter FromEnv()
            {
                var f = new SpotFilter();
                string s = System.Environment.GetEnvironmentVariable("PSX_PREVIEW_SPOTS");
                if (string.IsNullOrWhiteSpace(s)) return f;
                f.Raw = s.Trim();
                foreach (var t in s.Split(',', ';', ' ')) if (t.Trim().Length > 0) f.tokens.Add(t.Trim().ToLowerInvariant());
                f.All = f.tokens.Count == 0;
                return f;
            }
            /// <summary>Unset: the default set (<paramref name="byDefault"/>);
            /// "all": everything; else a name, a group, or a prefix*.</summary>
            public bool Takes(string name, string group, bool byDefault)
            {
                if (All) return byDefault;
                string n = name.ToLowerInvariant(), g = (group ?? "").ToLowerInvariant();
                foreach (var t in tokens)
                {
                    if (t == "all" || t == n || t == g) return true;
                    if (t.EndsWith("*") && n.StartsWith(t.Substring(0, t.Length - 1), System.StringComparison.Ordinal)) return true;
                }
                return false;
            }
        }

        enum ViewKind { Eye, Top, Profile, Along }

        /// <summary>
        /// One named view. <see cref="at"/> is a plan point (game metres, x
        /// east, z north) taken from the 2026-10-02 graph; the camera stands on
        /// the nearest edge whose name contains <see cref="road"/> (any road
        /// when empty), so an export that renumbers edges moves nothing.
        ///   Eye      windscreen at <see cref="at"/>, compass heading <see cref="hdg"/>
        ///   Top      straight down over <see cref="at"/>, ortho half-height <see cref="size"/>
        ///   Profile  side elevation of the road through <see cref="at"/>: the
        ///            roads only, heights x5, ortho half-height <see cref="size"/>
        ///   Along    on the road's lanes <see cref="back"/> m before <see cref="at"/>
        ///            (against its travel), looking at the road <see cref="look"/> m
        ///            before it - a merge seen from the mainline, near or far
        /// </summary>
        struct NamedView
        {
            public string name, group, road, what; public ViewKind kind; public Vector2 at;
            public float hdg, size, back, look, fov, rise; public int ring;
            /// <summary>Eye: stand on the ground lattice at <see cref="at"/>
            /// (a parking lot), not on the nearest road's height.</summary>
            public bool ground;
            /// <summary>Along: look at this plan point (0.4 m over the road)
            /// instead of down the lanes, when set.</summary>
            public Vector2 lookAt;
        }

        static NamedView Eye(string name, string group, float x, float z, string road, float hdg, string what) =>
            new NamedView { name = name, group = group, kind = ViewKind.Eye, at = new Vector2(x, z), road = road, hdg = hdg, fov = 60f, ring = 1, what = what };
        static NamedView Top(string name, string group, float x, float z, string road, float size, string what, int ring = 1) =>
            new NamedView { name = name, group = group, kind = ViewKind.Top, at = new Vector2(x, z), road = road, size = size, ring = ring, what = what };
        static NamedView Prof(string name, string group, float x, float z, string road, string what) =>
            new NamedView { name = name, group = group, kind = ViewKind.Profile, at = new Vector2(x, z), road = road, size = 112f, ring = 1, what = what };
        static NamedView Along(string name, string group, float x, float z, string road, float back, float look, float fov, int ring, string what) =>
            new NamedView { name = name, group = group, kind = ViewKind.Along, at = new Vector2(x, z), road = road, back = back, look = look, fov = fov, ring = ring, what = what };

        /// <summary>The plan's A1 view list: later packages shoot these by
        /// name (PSX_PREVIEW_SPOTS) without editing this file.</summary>
        static readonly NamedView[] NamedViews =
        {
            // W 5th St over I-77, the owner's example: node 2069 at the west end of the bridge, heading 134 (the frame he sent)
            Eye("w5th_owner", "w5th", -3363.2f, 5939.6f, "West 5th", 134f, "W 5th St at node 2069 (35.23801,-80.85467), heading 134, 1.2 m eye: the owner's frame, eastbound over I-77"),
            Eye("w5th_wb", "w5th", -3280.7f, 5880.9f, "West 5th", 314f, "W 5th St westbound from the east signal (n4121/n4122) across the bridge, heading 314"),
            Top("w5th_west_junction", "w5th", -3408.5f, 5993.0f, "West 5th", 45f, "the west signalised junction n4116/n4117: two fans and the grass between"),
            Top("w5th_top", "w5th", -3330.0f, 5918.0f, "West 5th", 70f, "W 5th St over I-77 from above (plan A2): one deck, two outer parapets, the raised median on to both signals"),
            // twin decks (B1/A2)
            Top("twin_i277", "twin", -2132.9f, 5956.5f, "I-277", 110f, "I-277 twin viaduct e1910/e1921: the longest union candidate"),
            Top("twin_e2437", "twin", -983.5f, 4472.1f, "I-277", 60f, "I-277 e2437/e2438: must stay two structures (the negative case)"),
            // vertical profiles (B2/B4/B7): side elevations, heights x5
            Prof("prof_w5th_i77", "profiles", -3327.4f, 5915.5f, "I-77", "I-77 under W 5th (e2132/e6608): the trench V and the 5 m carriageway step"),
            Prof("prof_i277_belk", "profiles", -2880.5f, 4085.9f, "I-277", "I-277 Belk Fwy under S College St"),
            Prof("prof_sunset_i77", "profiles", -2779.0f, 13646.4f, "I-77", "I-77 under Sunset Rd"),
            Prof("prof_johnston_i485", "profiles", -2545.1f, -13268.7f, "I-485", "I-485 under Johnston Rd"),
            // sags (plan L3): a driver's view down into a trench under a street
            Eye("sag_i77_w5th", "sags", -3392.6f, 5795.0f, "I-77", 40f, "I-77 (e6608) 150 m before W 5th St, heading 40, 1.2 m eye: down into the trench and up out of it (plan L3: comfort sags)"),
            // road heights from lidar (leftover item 1, 2026-10-03): the dips under bridges, BEFORE and AFTER at one pose
            Eye("ht_i277_college", "heights", -2969.7f, 4155.2f, "I-277", 133f, "I-277 eastbound (e2027) 110 m before the S College St bridge, heading 133, 1.2 m eye: the road down under the bridge and up again"),
            Eye("ht_i77_w5th", "heights", -3386.1f, 5811.5f, "I-77", 35f, "I-77 northbound (e6608) 110 m before the W 5th St bridges, heading 35, 1.2 m eye"),
            Eye("ht_trade_i77", "heights", -3586.5f, 5738.2f, "Trade", 105f, "W Trade St eastbound (e9613) 100 m before the I-77 bridges, heading 105, 1.2 m eye: the real street dips ~2.6 m under them"),
            // junction grade transitions (leftover item 1 finish, 2026-10-03): two of the worst LAUNCH junctions before the landings
            Eye("jg_mint_w4th", "jgrade", -2615.6f, 4925.1f, "Mint", 229f, "S Mint St southwest-bound 45 m before W 4th St (node 7288, signal), heading 229, 1.2 m eye: +9% into the junction, +3% out of it"),
            Eye("jg_westblvd_5192", "jgrade", -9764.4f, 2272.2f, "West Boulevard", 56f, "West Blvd eastbound (e6687) 80 m before node 5192 where the carriageways join, heading 56, 1.2 m eye: level into the junction, -6% out of it"),
            // merges (B5/A7/A8)
            Along("merge_i85_e3031_near", "merges", 9723.9f, 20811.8f, "I-85", 200f, 120f, 60f, 1, "the I-85 entrance e3031 (node 3268) from 30 m before its merge zone"),
            Along("merge_i85_e3031_far", "merges", 9723.9f, 20811.8f, "I-85", 570f, 120f, 30f, 2, "the same merge from 400 m back, 30 deg lens: z-fight / pop-in at range"),
            Along("merge_i77_e8", "merges", -10797.4f, -10724.0f, "I-77", 150f, 0f, 60f, 1, "the style-T merge of e8 into I-77 at node 14"),
            Along("merge_i77_node0", "merges", -10817.4f, -10791.7f, "I-77", 150f, 0f, 60f, 1, "the I-77 southbound diverge at node 0 (e0 off e11212)"),
            // merge zones (roads pass L5): the ramp ends at its nose, the host carries the lane
            Along("zone_i77_e180", "zones", -2661f, 6547f, "I-77", 110f, 0f, 60f, 1, "I-77 at the loop entrance e180 (node 315): style T, the aux lane run on 137 m past the node (AASHTO), then the 90 m taper"),
            Along("zone_i277_e172", "zones", -2424f, 3649f, "I-277", 120f, 0f, 60f, 1, "I-277 at the entrance e172 (node 299): style G, OSM's added lane carried from the nose N, 70 m before the node"),
            Along("zone_us74_e439", "zones", -1085f, 4134f, "US 74", 100f, 0f, 60f, 1, "US 74 at the exit e439 (node 794): style G, the exit lane carried to the nose 60 m past the node"),
            // lane-use paint (roads pass L6): turn-only lanes, arrows, ONLY
            Top("marks_mint_top", "marks", -3025.0f, 4548.0f, "Mint", 26f, "South Mint St (e14138/e13319, two-way, cls 3) from above: turn-only lanes with a solid white line to the through lanes, an arrow and ONLY in each, ending 3 m before the junction"),
            // junctions (A3/A4/A10/A11)
            Top("fork_n9862", "junctions", -9391f, -7086f, "", 110f, "ramp fork n9862 e8180/e8181: 196 m drawn inside each other"),
            Top("cluster_ntryon_harris", "junctions", 6189.1f, 13447.0f, "Tryon", 60f, "N Tryon St x W T Harris Blvd: four fans, the median grass through the box"),
            Top("cluster_mallard_harris", "junctions", 3545.7f, 14893.7f, "Mallard", 60f, "Mallard Creek Rd x W T Harris Blvd: a grass diamond in the box"),
            Top("cluster_pineville_carmel", "junctions", -2497.6f, -10765.0f, "Pineville", 60f, "Pineville-Matthews Rd x Carmel Rd: four fans overlapping in the middle"),
            // roads pass L7: curb returns, one paved area, crosswalks at a tier-1 signal
            Top("jn_trade_tryon", "junctions", -2316f, 4733f, "Tryon", 55f, "Trade St x Tryon St (the Square, 35.2271,-80.8431): curb returns, crosswalks on every arm, stop bars behind them"),
            // paint (B3/A5/A8)
            Top("paint_morehead_e1427", "paint", -4552.9f, 4905.2f, "Morehead", 25f, "W Morehead St e1427: lanes=3 f/b=2/1 drawn as a TWLTL"),
            Top("paint_mtholly_e1967", "paint", -15328.2f, 12916.9f, "Mount Holly", 25f, "Mount Holly Rd e1967: lanes=4 f/b=1/3 drawn 2|2"),
            Top("paint_i85_mw6_e2121", "paint", 8632.2f, 19445.4f, "I-85", 32f, "I-85 mw6 e2121: the widest freeway lines"),
            Top("paint_stryon_e11444", "paint", -2462.3f, 4609.9f, "Tryon", 25f, "S Tryon St e11444"),
            Top("paint_westblvd_e10174", "paint", -10258.6f, 1588.6f, "West Boulevard", 25f, "West Blvd e10174"),
            // lateral (B5/B6/A6)
            Top("lat_pineville_10874", "lateral", 2629.0f, -9877.9f, "Pineville", 40f, "Pineville-Matthews Rd node 10874: lanes jump 9 m across the junction"),
            Top("lat_steele_14505", "lateral", -14568.5f, -5350.2f, "Steele Creek", 40f, "Steele Creek Rd node 14505 (signal): tw3t/tw4 jog"),
            Top("lat_ntryon_11669", "lateral", -640.0f, 6268.3f, "Tryon", 40f, "N Tryon St node 11669: a lane drop of 7 m"),
            Top("lat_gleneagles_20141", "lateral", -1822.6f, -7350.8f, "Gleneagles", 30f, "Gleneagles Rd e20141: a 25 m tw5t piece that bulges"),
            // lanes line up (roads pass L4)
            Top("relay_mcdowell_12098", "lateral", -1212.0f, 4296.0f, "McDowell", 35f, "N McDowell St node 12098: a 2+1 split meeting a 1+2 one - the centre line moves across over the MUTCD length (the relay) instead of jumping a lane"),
            Top("lat_elizabeth_6995", "lateral", -1315.8f, 3738.4f, "Elizabeth", 40f, "Elizabeth Ave x N Kings Dr node 6995: tw2 into a tw4 that adds its lanes for the other direction - the through lanes line up across the junction"),
            // the owner's I-277 race (hotfix 2026-10-03, "90 degree concrete formations on 277 ... they
            // ended my race"): the Uptown Loop's first two kilometres, at the chase camera's height
            // (2.6 m), 35 m before each spot looking 10 m past it
            new NamedView { name = "i277_a_gore", group = "i277", kind = ViewKind.Along, at = new Vector2(-1561.6f, 5368.8f), road = "I-277", back = 10f, look = 0f, fov = 60f, ring = 1, rise = 1.4f,
                            lookAt = new Vector2(-1564.6f, 5361.8f),
                            what = "I-277 (e2311, Uptown Loop ~650 m, the owner's 0:15): the gore nose of the exit deck e2382 on the right - a rail block square across the nose, facing the flush gore" },
            new NamedView { name = "i277_b_union_end", group = "i277", kind = ViewKind.Along, at = new Vector2(-1234.1f, 5021.3f), road = "I-277", back = 22f, look = 0f, fov = 60f, ring = 1, rise = 1.4f,
                            lookAt = new Vector2(-1226.7f, 5026.5f),
                            what = "I-277 (e6403, Uptown Loop ~1,130 m, the owner's 0:45): the twin decks' union median ending on the left - the centre Jersey's square cap, then two edge Jerseys round a 2.6 m grass median" },
            new NamedView { name = "i277_c_median", group = "i277", kind = ViewKind.Along, at = new Vector2(-1051.7f, 4345.2f), road = "I-277", back = 22f, look = 0f, fov = 60f, ring = 1, rise = 1.4f,
                            lookAt = new Vector2(-1047.8f, 4340.4f),
                            what = "I-277 (e2144, Uptown Loop ~1,920 m, the owner's 1:02): the left exit's gore - a median Jersey starting square beside the yellow line, grass behind it at its top" },
            // stray barrier pieces (hotfix 2026-10-03, "concrete median blocks between roads where they
            // shouldn't exist"): the short barrier census's shortest freeway piece, 25 m before it
            new NamedView { name = "short_i485_e14173", group = "short", kind = ViewKind.Along, at = new Vector2(-13766f, -1400f), road = "I-485", back = 25f, look = 0f, fov = 60f, ring = 1, rise = 1.4f,
                            lookAt = new Vector2(-13766f, -1400f),
                            what = "I-485 (e14173, s 4-12) at its ramp: a 7.8 m cut wall standing alone on the right (the short barrier census's shortest freeway piece)" },
            // parapet and rail ends on bridge approaches (leftover item 2, 2026-10-03): driver's eye, the
            // right lane, ~28 m before an approach rail's start (the drive audit's RAIL ENDS list); with w5th_wb
            Eye("parapet_i277_e10558", "parapets", -2306.4f, 3532.3f, "I-277", 133f, "I-277 (e10558, Belk Fwy) right lane 28 m before the approach rail starting at s 178 on the right, heading 133"),
            Eye("parapet_e12th_e2325", "parapets", -1465.3f, 5353.7f, "12th", 314f, "E 12th St (e2325, one-way) right lane 32 m before the approach rails of the bridge e1329 starting at s 68 on both sides, heading 314"),
            Eye("parapet_graham_e1941", "parapets", -1868.3f, 5815.6f, "Graham", 48f, "N Graham St (e1941) northbound inner lane between its two bridges (e1937, e1942): the approach rails end at s 20 and start again at s 38, heading 48"),
            // minor streets, cul-de-sacs, parking lots (roads pass L8)
            Top("minor_rozzelles_1178", "minor", -3915.3f, 6467.5f, "Rozzelles", 22f, "Rozzelles Ferry Rd at Whitehaven Ave (node 1178, unsignalised) from above: the centre and far edge lines run on across the side street's mouth, the near edge line breaks for it (plan A13)"),
            Top("minor_bulb_victorian", "minor", -3517.0f, 2391.9f, "Victorian", 26f, "Victorian Place's turning circle (node 17457): the street ends in a 12.2 m bulb (plan A17)"),
            Top("minor_lot_139", "minor", -1562.8f, 5101.9f, "", 45f, "a surface lot off N Davidson St (lot 139): the lot laid into the ground at the street's level, its stall lines, the concrete apron at its entrance (plan B9/B10)"),
            // parking aisles and islands (leftover item 4, 2026-10-03): inside lot 139 on OSM aisle 914753376
            new NamedView { name = "lot_eye_139", group = "lots", kind = ViewKind.Eye, at = new Vector2(-1581.0f, 5098.2f), road = "", hdg = 49f, fov = 60f, ring = 1, ground = true,
                            what = "inside the lot off N Davidson St (lot 139) on OSM parking aisle 914753376, heading 49, 1.2 m over the lot: the aisle and the stall rows along it" },
            // junction boxes (leftover item 3, 2026-10-03): the paved junction in its main road's
            // surface (with jn_trade_tryon), and Little Rock Road under I-85 from the driver's seat
            Eye("jb_tryon_eye", "jbox", -2338.2f, 4710.4f, "Tryon", 50f, "South Tryon St northeast-bound, inner lane, 38 m before Trade St (the Square, node 1026), heading 50, 1.2 m eye"),
            Eye("lr_nb_eye", "jbox", -11039.7f, 6228.0f, "Josh Birmingham", 21f, "N Josh Birmingham Pkwy northbound (e9838) 30 m before the signal at node 518, heading 21, 1.2 m eye: on into Little Rock Rd under the I-85 decks"),
            Eye("lr_nb_under", "jbox", -11022.0f, 6275.0f, "Josh Birmingham", 20f, "N Josh Birmingham Pkwy northbound (e9839) 20 m before node 429, heading 20, 1.2 m eye: under the I-85 decks, the ramp crossovers ahead"),
            Eye("lr_sb_eye", "jbox", -11005.7f, 6372.8f, "Little Rock", 201f, "Little Rock Rd southbound (e3717) 35 m before the signal at node 426, heading 201, 1.2 m eye: towards I-85"),
            Eye("lr_sb_under", "jbox", -11023.5f, 6324.0f, "Little Rock", 199f, "Little Rock Rd southbound (e10076) between nodes 426 and 521, heading 199, 1.2 m eye: under the I-85 decks"),
            Eye("lr_ramp_e", "jbox", -10985.0f, 6338.0f, "", 222f, "the I-85 off-ramp terminal (e9799) just past its signal at node 11067, heading 222, 1.2 m eye: across Little Rock Rd under the decks"),
            Eye("lr_ramp_w", "jbox", -11070.1f, 6263.6f, "", 56f, "the I-85 off-ramp (e269) 40 m before node 485 on Little Rock Rd, heading 56, 1.2 m eye"),
            Top("lr_top", "jboxdbg", -11020f, 6300f, "Little Rock", 70f, "Little Rock Rd under I-85 from above (the decks hide the junction)"),
            new NamedView { name = "lr_under_top", group = "jboxdbg", kind = ViewKind.Top, at = new Vector2(-11020f, 6300f), road = "Little Rock", size = 70f, ring = 1, rise = 3.2f,
                            what = "INTERNAL: the junction under the I-85 decks seen from 3.2 m over Little Rock Rd (the decks above the camera)" },
            new NamedView { name = "ar_under_top", group = "jboxdbg", kind = ViewKind.Top, at = new Vector2(-8165f, -5195f), road = "Arrowood", size = 70f, ring = 1, rise = 3.2f,
                            what = "INTERNAL: W Arrowood Rd under I-77 (ramp terminals n702/n703/n3899/n3900) seen from 3.2 m over the road" },
            Eye("ar_eye", "jboxdbg", -8238.8f, -5161.8f, "Arrowood", 119f, "W Arrowood Rd eastbound (e2693) 25 m before node 3899, heading 119, 1.2 m eye: under I-77"),
            // houses (leftover item 6, 2026-10-03): driveways and lots graded under the houses - the
            // house audit's worst sloped streets and a flat one, driver's eye 1.2 m on the street
            Along("houses_sherwood_eye", "houses", -847.3f, 453.7f, "Sherwood", 35f, 0f, 60f, 1, "Sherwood Avenue (e24832, 17 houses, mean fall 1.4 m) 35 m before the prefab house at (-847, 454) whose foundation showed 2.5 m on its downhill side"),
            Along("houses_frazier_eye", "houses", -3636.3f, 5844.2f, "Frazier", 35f, 0f, 60f, 1, "Frazier Avenue (e24215, owner box) 35 m before the real house at (-3636, 5844) standing 4.0 m into its slope"),
            Along("houses_sylvania_eye", "houses", -1170.1f, 6486.1f, "Sylvania", 40f, 0f, 60f, 1, "Sylvania Avenue (e22031, owner box, 18 houses on flat lots) 40 m before its middle"),
            Top("houses_sherwood_top", "houses", -820.0f, 470.0f, "Sherwood", 60f, "the Sherwood Avenue block from above: prefab houses, real footprints and the fill behind them"),
            // stray walls on I-277 (owner 2026-10-04, "stray medians on 277"): the Uptown Loop's
            // south-east quarter at 1.2 m on the route's own line, heading along it
            Eye("i277b_1750", "i277b", -985.4f, 4500.6f, "I-277", 198f, "I-277 (e2438, Uptown Loop 1,750 m) heading 198: the deck by E 10th St, its right parapet in the outside lane at 1,781 m"),
            Eye("i277b_2745", "i277b", -1534.5f, 3715.4f, "I-277", 219f, "I-277 (e14177, Uptown Loop 2,745 m) heading 219: the race line 20 m before the median start at 2,765 m (waypoint 692, e2344/e1393, by E 4th St)"),
            Eye("i277b_3200", "i277b", -1857.0f, 3403.7f, "I-277", 253f, "I-277 (e2341, Uptown Loop 3,200 m) heading 253: the 2.6 m connector e2342 onto the deck e1500, the entrance e196 joining on the right"),
            Eye("i277b_2360", "i277b", -1274.5f, 3997.5f, "I-277", 237f, "I-277 (e2144, Uptown Loop 2,360 m) heading 237: onto the deck e2316, US 74's deck e2366 on the right"),
            Eye("i277b_2440", "i277b", -1336.5f, 3947.3f, "I-277", 221f, "I-277 (e2321, Uptown Loop 2,440 m) heading 221: US 74 e2367 closing in on the right to the merge at 2,549 m"),
            Eye("i277b_2510", "i277b", -1383.0f, 3895.0f, "I-277", 223f, "I-277 (e2321, Uptown Loop 2,510 m) heading 223: the US 74 merge and the deck e1412 ahead"),
            Eye("i277b_2725", "i277b", -1521.8f, 3731.0f, "I-277", 219f, "I-277 (e14177, Uptown Loop 2,725 m) heading 219: the race line 40 m before the median start at 2,765 m (waypoint 692, e2344/e1393)"),
            Eye("i277b_2960", "i277b", -1670.3f, 3548.8f, "I-277", 220f, "I-277 (e2340, Uptown Loop 2,960 m) heading 220: the entrance e1408 closing in on the right"),
            Eye("i277b_3170", "i277b", -1828.9f, 3414.2f, "I-277", 246f, "I-277 (e2341, Uptown Loop 3,170 m) heading 246: the entrance e196 and the deck e1500 ahead"),
            Eye("i277b_3490", "i277b", -2136.9f, 3429.1f, "I-277", 299f, "I-277 (e1499, Uptown Loop 3,490 m) heading 299: the deck e1490 ahead"),
            Eye("i277b_3600", "i277b", -2224.2f, 3495.4f, "I-277", 315f, "I-277 (e9905, Uptown Loop 3,600 m) heading 315: the exit e9767 leaving on the right"),
            // the owner's W Trade St frame (2026-10-04, free roam, night): "a thin layer of dirt and I can see under the dirt and the road to the right"
            Eye("tradedirt_i77_eb", "tradedirt", -3470f, 5710f, "Trade", 100f, "W Trade St eastbound (e8966) east of the I-77 bridges, heading 100: the off-ramp e227 closing in on the right"),
            Eye("tradedirt_syc_wb", "tradedirt", -3300f, 5667f, "Trade", 285f, "W Trade St westbound (e15298) past Sycamore St, heading 285: the I-77 ramps' triangle ahead on the right"),
            Eye("tradedirt_graham_wb", "tradedirt", -2600f, 5045f, "Trade", 318f, "W Trade St outbound (e5785) 60 m before Graham St, heading 318: the median's end at the junction"),
            Eye("tradedirt_4115_nb", "tradedirt", -3600f, 5748f, "Trade", 290f, "W Trade St westbound (e9613) toward the I-77 ramps at node 4115, heading 290"),
            Top("tradedirt_syc_top", "tradedirt", -3360f, 5680f, "Trade", 45f, "W Trade St from Sycamore St to the I-77 off-ramp e227 from above"),
            Eye("tradedirt_nose_nw", "tradedirt", -2632f, 5078f, "Trade", 318f, "W Trade St at Graham St: in the median 25 m before its nose, heading 318 - the slot between the junction paving and the inbound verge (-2648,5096)"),
            Eye("tradedirt_nose_sw", "tradedirt", -2662f, 5114f, "Graham", 140f, "Graham St at W Trade St, heading 140 down the median: the slot at the nose from the junction"),
            Top("tradedirt_nose_top", "tradedirt", -2650f, 5100f, "Trade", 14f, "the W Trade St median nose at Graham St from above (the slot at -2648,5096)"),
            Eye("tradedirt_13384_nw", "tradedirt", -3655f, 5769f, "Trade", 318f, "W Trade St (e13384, two-way) north-west from node 4115, heading 318: land standing up to 0.57 m over the pavement on its left (5.4 m out)"),
            Eye("tradedirt_13384_se", "tradedirt", -3715f, 5823f, "Trade", 138f, "W Trade St (e13384) south-east toward node 4115, heading 138: the same land on the right"),
            Top("tradedirt_13384_top", "tradedirt", -3688f, 5795f, "Trade", 30f, "W Trade St (e13384) from above: the land over its south-west edge"),
            new NamedView { name = "fascia_check_1", group = "fascia_check", kind = ViewKind.Eye, at = new Vector2(-1903.5f, 3416f), road = "", hdg = 180f, fov = 60f, ring = 1, lookAt = new Vector2(-1904.5f, 3401.4f), what = "a deck edge the closing pass gave a fascia (-1904,3401), from 15 m north" },
            new NamedView { name = "fascia_check_2", group = "fascia_check", kind = ViewKind.Eye, at = new Vector2(-1222f, 3970f), road = "", hdg = 330f, fov = 60f, ring = 1, lookAt = new Vector2(-1237f, 3994.2f), what = "a deck edge the closing pass gave a fascia (-1237,3994), from 28 m south-east" },
            new NamedView { name = "tradedirt_nose_low", group = "tradedirt_photo", kind = ViewKind.Eye, at = new Vector2(-2643.8f, 5100.1f), road = "Trade", hdg = 230f, fov = 60f, ring = 1, rise = -0.5f, lookAt = new Vector2(-2651f, 5099f), what = "W Trade St outbound (e5785) at Graham St, 0.7 m eye, looking straight at the median nose (-2649,5097.5): the junction paving edge over the slot (BEFORE) or its chord verge (AFTER)" },
            new NamedView { name = "tradedirt_verge_i277", group = "tradedirt_photo", kind = ViewKind.Eye, at = new Vector2(-2921.9f, 4100.7f), road = "I-277", hdg = 312f, fov = 60f, ring = 1, rise = -0.5f, lookAt = new Vector2(-2927.8f, 4106.1f), what = "I-277 (e2027, Belk Fwy by S College St), 0.7 m eye, looking at a verge end 7.8 m off the carriageway that stood 2.25 m over the ground (BEFORE) or its closing face (AFTER)" },
            Top("tradedirt_pit_top", "tradedirt", -3322f, 5712f, "Trade", 22f, "north of W Trade St at Sycamore St: the ground 5 m under the street at (-3322,5716) from above"),
            Eye("tradedirt_pit_nb", "tradedirt", -3318f, 5680f, "Trade", 0f, "W Trade St at Sycamore St, heading 0 (north) toward the low ground at (-3322,5716)"),
            Eye("cutkeep_i77_nb", "cutkeep", -3334f, 5864f, "I-77", 40f, "I-77 northbound (e6608) 40 m before W 5th St, heading 40: the 24 m cut wall at s 198..222 on the right (hotfix 2026-10-03 dropped it; the land above stood open)"),
            Eye("cutkeep_i77_sb", "cutkeep", -3306f, 5952f, "I-77", 217f, "I-77 southbound (e2132) under W 5th St, heading 217: the 20 m cut wall at s 89..109 on the right"),
            Top("cutkeep_i77_top", "cutkeep", -3326f, 5915f, "I-77", 28f, "I-77 in its trench under W 5th St from above: the two short cut walls"),
        };

        /// <summary>The nearest point on an edge whose name contains
        /// <paramref name="road"/> (any road when empty) within 120 m.</summary>
        static bool SnapNamed(CityMap map, Vector2 p, string road, out CityMap.Edge edge, out float s)
        {
            edge = null; s = 0f;
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(p - Vector2.one * 120f, p + Vector2.one * 120f, segs);
            float best = float.MaxValue;
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (si + 1 >= e.pts.Length) continue;
                if (!string.IsNullOrEmpty(road) && (e.name == null || e.name.IndexOf(road, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                Vector2 q0 = e.pts[si], d = e.pts[si + 1] - q0;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, d) / L2) : 0f;
                float dd = Vector2.Distance(p, q0 + d * t);
                if (dd < best) { best = dd; edge = e; s = e.s[si] + Mathf.Sqrt(L2) * t; }
            }
            return edge != null;
        }

        /// <summary>Walk back along the road's direction of travel from arc s of
        /// e by <paramref name="dist"/>, through the node at each edge's start
        /// onto the edge that runs into it (same name first). Point, travel
        /// direction, edge and arc where it stopped.</summary>
        static void WalkBack(CityMap map, CityMap.Edge e, float s, float dist, out Vector2 p, out Vector2 dir, out CityMap.Edge at, out float sAt)
        {
            int guard = 0;
            while (dist > s && guard++ < 40)
            {
                dist -= s;
                int n = e.a;
                CityMap.Edge prev = null;
                foreach (int ei in map.nodeEdges[n])
                {
                    var o = map.edges[ei];
                    if (o == e || o.b != n || o.a == o.b) continue;
                    if (prev == null || (o.name == e.name && prev.name != e.name) || (!o.link && prev.link)) prev = o;
                }
                if (prev == null) { dist = s; break; }
                e = prev; s = e.length;
            }
            sAt = Mathf.Max(0f, s - dist);
            at = e;
            p = LineModel.LanePoint(e, sAt);
            dir = e.TangentAt(sAt);
        }

        static bool ShootNamed(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                               GameObject world, List<GameObject> roots, string dir, NamedView v, System.Text.StringBuilder log)
        {
            if (!SnapNamed(map, v.at, v.road, out var e, out float s)) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 eye, look; float ortho = 0f, fov = v.fov > 0f ? v.fov : 60f, near = 0.3f;
            Vector2 ringAt = v.at; int ring = v.ring;
            float y = e.YAt(s);
            var vex = (GameObject)null;
            switch (v.kind)
            {
                case ViewKind.Eye:
                {
                    // the owner's camera: on the point itself, at the road's height there
                    float h = v.hdg * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
                    eye = new Vector3(v.at.x, y + 1.2f + v.rise, v.at.y);   // rise: a lower eye (2026-10-04 photos)
                    look = v.lookAt != Vector2.zero ? new Vector3(v.lookAt.x, y + 0.2f, v.lookAt.y) : eye + d * 60f + Vector3.down * 1.0f;
                    near = 0.2f;   // the chase camera's (ChaseCamera.cs), so depth precision matches the game
                    break;
                }
                case ViewKind.Top:
                    // rise > 0 (internal, leftover item 3): from that far over the road, under any deck
                    eye = new Vector3(v.at.x, y + (v.rise > 0f ? v.rise : 300f), v.at.y); look = eye + Vector3.down; ortho = v.size;
                    if (v.rise > 0f) near = 0.05f;
                    break;
                case ViewKind.Profile:
                {
                    var t = e.TangentAt(s);
                    var side = new Vector2(t.y, -t.x);   // the right of the road's travel
                    eye = new Vector3(v.at.x + side.x * 400f, y, v.at.y + side.y * 400f);
                    look = new Vector3(v.at.x, y, v.at.y);
                    ortho = v.size;
                    break;
                }
                default:   // Along
                {
                    WalkBack(map, e, s, v.back, out var pe, out var de, out var ee, out float se);
                    WalkBack(map, e, s, v.look, out var pl, out _, out var el, out float sl);
                    eye = new Vector3(pe.x, ee.YAt(se) + 1.2f + v.rise, pe.y);
                    look = v.lookAt != Vector2.zero ? new Vector3(v.lookAt.x, el.YAt(sl) + 0.4f, v.lookAt.y)
                                                     : new Vector3(pl.x, el.YAt(sl) + 1.0f, pl.y);
                    if ((look - eye).sqrMagnitude < 1f) look = eye + new Vector3(de.x, 0f, de.y) * 50f;
                    ringAt = new Vector2(0.5f * (eye.x + look.x), 0.5f * (eye.z + look.z));
                    near = 0.2f;
                    break;
                }
            }
            var tiles = BuildRing(map, trims, buildings, world, ringAt, ring, out var stats);
            roots.AddRange(tiles);
            if (v.ground && v.kind == ViewKind.Eye)
            {
                // on the lot's own surface (the lattice the tiles just built)
                float gy = CityMeshes.LatticeAt(map, v.at.x, v.at.y) + 1.2f;
                look.y += gy - eye.y; eye.y = gy;
            }
            if (v.kind == ViewKind.Profile)
            {
                // the roads alone, heights x5 about the road's own height there
                const float Ve = 5f;
                vex = new GameObject("~vex");
                vex.transform.position = new Vector3(0f, -(Ve - 1f) * y, 0f);
                vex.transform.localScale = new Vector3(1f, Ve, 1f);
                foreach (var t in tiles)
                {
                    t.transform.SetParent(vex.transform, false);   // keep the LOCAL pose, so the parent's scale applies
                    foreach (Transform c in t.transform) if (c.name != "roads" && c.name != "barriers") c.gameObject.SetActive(false);
                }
                roots.Add(vex);
            }
            Debug.Log($"[CityPreview] {v.name}: {stats}");
            Shoot(dir, v.name, eye, Quaternion.LookRotation(look - eye, v.kind == ViewKind.Top ? Vector3.forward : Vector3.up), ortho, 3000f, 1280, 720, fov, near);
            log.Append(string.Format(inv, "{0}\t{1}\t{2:0.0}\t{3:0.00}\t{4:0.0}\t{5:0.0}\t{6:0.00}\t{7:0.0}\t{8:0}\t{9:0}\t{10} (e{11} '{12}' s={13:0}) {14}\n",
                v.name, v.group, eye.x, eye.y, eye.z, look.x, look.y, look.z, ortho, fov, v.what, e.index, e.name, s, CityAudit.LatLon(v.at.x, v.at.y)));
            foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t);
            if (vex != null) Object.DestroyImmediate(vex);
            roots.RemoveAll(r => r == null);
            return true;
        }

        /// <summary>
        /// THE STAIRCASES: the worst places the overlap census found a road
        /// standing up out of another road's lanes, photographed from above,
        /// from the windscreen of the road being intruded on, and from the
        /// side. The spots come from PSX_CITY_SPOTS ("x,z;x,z;...") when it is
        /// set, so a fix can be shot from exactly where the fault was; else
        /// from the census, and the list is written beside the shots.
        /// </summary>
        public static void RunStaircases()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityPreview] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City");
            Directory.CreateDirectory(dir);
            PSXRacingBuilder.EnsureCityTextures();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // (x, z, host edge, the other edge)
            var spots = new List<(Vector2 at, int host, int other)>();
            string env = System.Environment.GetEnvironmentVariable("PSX_CITY_SPOTS");
            if (!string.IsNullOrEmpty(env))
                foreach (var part in env.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length == 4 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, inv, out float x)
                                      && float.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out float z)
                                      && int.TryParse(f[2], out int h) && int.TryParse(f[3], out int o))
                        spots.Add((new Vector2(x, z), h, o));
                }
            else
            {
                var worst = new List<CityAudit.OverlapPair>();
                CityAudit.OverlapCensus(map, trims, false, worst);
                foreach (var w in worst)
                {
                    if (!w.branch) continue;
                    if (spots.Exists(q => Vector2.Distance(q.at, w.at) < 250f)) continue;
                    var e = map.edges[w.e]; var o = map.edges[w.o];
                    // the host is the through road: not the link, else the wider
                    bool eHost = e.link != o.link ? !e.link : e.width >= o.width;
                    spots.Add((w.at, eHost ? w.e : w.o, eHost ? w.o : w.e));
                    if (spots.Count >= 8) break;
                }
                var sb = new System.Text.StringBuilder();
                foreach (var q in spots)
                    sb.Append(sb.Length > 0 ? ";" : "").Append(q.at.x.ToString("0", inv)).Append(',').Append(q.at.y.ToString("0", inv))
                      .Append(',').Append(q.host).Append(',').Append(q.other);
                File.WriteAllText(Path.Combine(dir, "staircase_spots.txt"), sb.ToString());
            }

            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var world = new GameObject("~CityStairWorld");
            try
            {
                for (int i = 0; i < spots.Count; i++)
                {
                    var (at, hi, oi) = spots[i];
                    if (hi < 0 || hi >= map.edges.Length || oi < 0 || oi >= map.edges.Length) continue;
                    var host = map.edges[hi];
                    var other = map.edges[oi];
                    CityElevation.ProjectOn(host, at, out float hs);
                    CityElevation.ProjectOn(other, at, out float os);
                    var tiles = BuildRing(map, trims, buildings, world, at, 1, out var stats);
                    Debug.Log($"[CityPreview] stair{i} at ({at.x:0},{at.y:0}) {CityAudit.LatLon(at.x, at.y)} host e{hi} '{host.name}' y {host.YAt(hs):0.00} / e{oi} '{other.name}'{(other.link ? " L" : "")} y {other.YAt(os):0.00}: {stats}");
                    float y = host.YAt(hs);
                    Shoot(dir, $"stair{i}_top", new Vector3(at.x, y + 200f, at.y), Quaternion.Euler(90f, 0f, 0f), ortho: 70f, w: 1280, h: 720);
                    // along the host, both ways, from 70 m out at the windscreen
                    foreach (var (tag, back) in new[] { ("ahead", 70f), ("behind", -70f) })
                    {
                        float s0 = Mathf.Clamp(hs - back, 0f, host.length);
                        var p0 = host.PointAt(s0);
                        var fwd2 = (host.PointAt(hs) - p0);
                        if (fwd2.sqrMagnitude < 1f) fwd2 = host.TangentAt(hs) * Mathf.Sign(back);
                        fwd2.Normalize();
                        var fwd = new Vector3(fwd2.x, 0f, fwd2.y);
                        var eye = new Vector3(p0.x, host.YAt(s0) + 1.4f, p0.y);
                        Shoot(dir, $"stair{i}_{tag}", eye, Quaternion.LookRotation(fwd + Vector3.down * 0.03f), ortho: 0f, w: 1280, h: 720);
                    }
                    // across the host at the other road, from the far side, low
                    var t = host.TangentAt(hs);
                    var right = new Vector2(-t.y, t.x);
                    float sideOf = Mathf.Sign(Vector2.Dot(other.PointAt(os) - host.PointAt(hs), right));
                    if (sideOf == 0f) sideOf = 1f;
                    var hp = host.PointAt(hs);
                    var eyeP = hp - right * sideOf * 40f - t * 25f;
                    var eyeL = new Vector3(eyeP.x, y + 6f, eyeP.y);
                    var target = new Vector3(at.x, y + 1f, at.y);
                    Shoot(dir, $"stair{i}_side", eyeL, Quaternion.LookRotation(target - eyeL), ortho: 0f, w: 1280, h: 720);
                    foreach (var tile in tiles) Object.DestroyImmediate(tile);
                }
            }
            finally { Object.DestroyImmediate(world); }
            Debug.Log($"[CityPreview] staircases: {spots.Count} spots shot to {dir}");
        }

        // =====================================================================
        //  The uptown reference views (2026-10-04, uptown visual pass A)
        // =====================================================================

        /// <summary>One aerial camera: an eye over the ground (metres above
        /// the DEM there), an aim point (metres above the DEM there) and a
        /// vertical field of view. <see cref="nearInterchange"/> stands the
        /// eye at that offset (east, north) from the I-77 / I-277 interchange
        /// nearest the eye's lat/lon, so the ramps are where the map has them.</summary>
        struct SkyView
        {
            public string name, what;
            public double eyeLat, eyeLon, aimLat, aimLon;
            public float eyeAgl, aimAgl, fov;
            public bool nearInterchange; public Vector2 offset;
        }

        /// <summary>
        /// THE UPTOWN SKYLINE, AS THE OWNER PHOTOGRAPHED IT: five aerial
        /// cameras matched to his five reference photographs by their
        /// geography (where the photographer stood, what is in the middle of
        /// the frame, the lens), shot through the GAME's camera in the
        /// Charlotte scene - its sky, its hour, its grade and dither - at a
        /// clear noon and at dusk. The photographs are only LOOKED at; nothing
        /// here came from them but a place, a direction and a lens. Neutral
        /// names only (owner rule 2026-10-04: no brands anywhere in the game).
        ///
        /// The game streams two tiles round the player and fades at that ring's
        /// edge, so from these eyes the real game would show sky; the shots
        /// build the tiles along the line of sight and push the fade out to
        /// PSX_UPTOWN_FOGM metres (default 4000; 0 keeps the game's own) so the
        /// buildings can be judged. PSX_UPTOWN_REF=a,b shoots a subset,
        /// PSX_UPTOWN_HOURS=noon,sunset,dusk,night the hours (default noon,dusk).
        /// Headless: -executeMethod PSXRacing.EditorTools.CityPreview.RunUptownRef
        /// (after a scene build: it opens Charlotte.unity). PNGs land in
        /// Screenshots\uptownref_&lt;view&gt;_&lt;hour&gt;.png, cameras in
        /// Screenshots\City\uptown_ref_spots.txt.
        /// </summary>
        static readonly SkyView[] UptownRefViews =
        {
            new SkyView { name = "sw_ramps", nearInterchange = true, offset = new Vector2(-200f, -200f),
                eyeLat = 35.2195, eyeLon = -80.8585, eyeAgl = 90f, aimLat = 35.22741, aimLon = -80.84217, aimAgl = 120f, fov = 35f,
                what = "ref 1: over the I-77 / I-277 interchange south-west of uptown, ramps in front, looking north-east at the towers" },
            new SkyView { name = "frame_over_stadium",
                eyeLat = 35.22620, eyeLon = -80.86000, eyeAgl = 60f, aimLat = 35.22395, aimLon = -80.84859, aimAgl = 215f, fov = 38f,
                what = "ref 2: west of the stadium across I-77, looking east-south-east up at the open-frame tower top over the stadium" },
            new SkyView { name = "spire_tryon",
                eyeLat = 35.23524, eyeLon = -80.84805, eyeAgl = 170f, aimLat = 35.22741, aimLon = -80.84217, aimAgl = 150f, fov = 50f,
                what = "ref 3: high over the north-west side of uptown, the church spire in front, the spired crown tower centre, the pointed tower right" },
            new SkyView { name = "crowns",
                eyeLat = 35.23253, eyeLon = -80.83540, eyeAgl = 200f, aimLat = 35.22758, aimLon = -80.84147, aimAgl = 225f, fov = 30f,
                what = "ref 4: north-east of the core at crown height, looking south-west: the spired crown and the silver crown, the open frame behind" },
            new SkyView { name = "sw_stadium",
                eyeLat = 35.22032, eyeLon = -80.85904, eyeAgl = 100f, aimLat = 35.22574, aimLon = -80.84805, aimAgl = 110f, fov = 45f,
                what = "ref 5: south-west of the stadium looking north-east over it: the brick headquarters, the open frame, the pyramid top, I-277 on the right" },
        };

        public static void RunUptownRef()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null) { Debug.LogError("[UptownRef] no Charlotte venue"); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[UptownRef] Charlotte.unity did not open (scene build first)"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[UptownRef] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();

            // views of the city, not of the car: no HUD, no player
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            if (player != null) player.SetActive(false);

            string onlyEnv = System.Environment.GetEnvironmentVariable("PSX_UPTOWN_REF");
            var only = string.IsNullOrEmpty(onlyEnv) ? null : new HashSet<string>(onlyEnv.Split(','));
            float fogM = 4000f;
            if (float.TryParse(System.Environment.GetEnvironmentVariable("PSX_UPTOWN_FOGM"), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float fe)) fogM = fe;

            // PSX_UPTOWN_HOURS=noon,sunset,dusk,night (the hour names; default noon,dusk)
            var hours = new List<int>();
            string hoursEnv = System.Environment.GetEnvironmentVariable("PSX_UPTOWN_HOURS");
            foreach (var hn in (string.IsNullOrEmpty(hoursEnv) ? "noon,dusk" : hoursEnv).Split(','))
            {
                int hi = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hn.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (hi >= 0) hours.Add(hi);
            }

            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && fogM > 0f) globals.fogScale = fogM / Mathf.Max(1f, TimeOfDay.All[TimeOfDay.Noon].fogFar);
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;   // clear

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var log = new System.Text.StringBuilder("shot\teye_x\teye_y\teye_z\taim_x\taim_y\taim_z\tfov\ttiles\twhat\n");
            var go = new GameObject("~uptownRefWorld");
            var world = go.AddComponent<CityWorld>();
            int shots = 0;
            try
            {
                foreach (var v in UptownRefViews)
                {
                    if (only != null && !only.Contains(v.name)) continue;
                    Vector2 e2 = CityRefSpots.LL(v.eyeLat, v.eyeLon), a2 = CityRefSpots.LL(v.aimLat, v.aimLon);
                    if (v.nearInterchange)
                    {
                        float best = float.MaxValue; Vector2 at = e2;
                        foreach (var ic in CityAudit.Interchanges(map, "I-277", "I-77"))
                        {
                            float d = Vector2.Distance(ic.at, e2);
                            if (d < best) { best = d; at = ic.at; }
                        }
                        e2 = at + v.offset;
                    }
                    var eye = new Vector3(e2.x, CityElevation.BaseY(e2.x, e2.y) + v.eyeAgl, e2.y);
                    var aim = new Vector3(a2.x, CityElevation.BaseY(a2.x, a2.y) + v.aimAgl, a2.y);

                    // the tiles: along the sight line from the eye to 700 m past
                    // the aim (two tiles either side), and uptown's core
                    Vector2 sight = (a2 - e2).normalized;
                    float len = Vector2.Distance(a2, e2) + 700f;
                    for (float s = 0f; s <= len; s += 220f)
                    {
                        var p = e2 + sight * s;
                        world.EnsureRing(new Vector3(p.x, 0f, p.y), 2);
                    }
                    world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 3);

                    foreach (int hour in hours)
                    {
                        TimeOfDay.Apply(hour, sun);
                        NightGlow.PreviewAll(hour >= TimeOfDay.Dusk);
                        if (globals != null) globals.Apply();
                        cam.fieldOfView = v.fov;
                        cam.farClipPlane = Mathf.Max(cam.farClipPlane, (fogM > 0f ? fogM : 3000f) + 500f);
                        // B4: the far skyline for this eye (what the built
                        // tiles draw is left to them)
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                        world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, $"uptownref_{v.name}_{TimeOfDay.All[hour].name.ToLowerInvariant()}",
                                                 eye, Quaternion.LookRotation(aim - eye));
                        shots++;
                    }
                    log.Append(string.Format(inv, "{0}\t{1:0.0}\t{2:0.0}\t{3:0.0}\t{4:0.0}\t{5:0.0}\t{6:0.0}\t{7:0}\t{8}\t{9} | eye {10}\n",
                        v.name, eye.x, eye.y, eye.z, aim.x, aim.y, aim.z, v.fov, go.transform.childCount, v.what, CityAudit.LatLon(e2.x, e2.y)));
                    world.DropAll();
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "uptown_ref_spots.txt"), log.ToString());
            Debug.Log($"[UptownRef] {shots} shots (fog edge {(fogM > 0f ? fogM.ToString("0") + " m" : "the game's")}) to Screenshots\\uptownref_*.png");
        }

        /// <summary>
        /// THE SKYLINE IN PLAY (Uptown B4): two eyes on I-77 about
        /// <c>PSX_SKYLINE_KM</c> (default 4) km south and north of uptown, at
        /// a chase camera's height over the carriageway, looking at the core
        /// - through the game's OWN draw distance and edge fade (no fog
        /// override, the scene's far plane) and the game's two-tile ring
        /// round the eye, at noon, dusk and night. PSX_SKYLINE=0 leaves the
        /// skyline off (the before). PNGs: Screenshots\skyline_&lt;view&gt;_&lt;hour&gt;[_off].png.
        /// Headless: -executeMethod PSXRacing.EditorTools.CityPreview.RunSkylinePlay
        /// </summary>
        public static void RunSkylinePlay()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[SkylinePlay] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[SkylinePlay] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            if (player != null) player.SetActive(false);
            bool on = System.Environment.GetEnvironmentVariable("PSX_SKYLINE") != "0";
            // PSX_SKYLINE_KM=3,4,5 (default 4), PSX_SKYLINE_HOURS=noon,dusk,night (the default)
            var kms = new List<float>();
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_SKYLINE_KM") ?? "4").Split(','))
                if (float.TryParse(t.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float kme)) kms.Add(kme);
            if (kms.Count == 0) kms.Add(4f);
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var hours = new List<int>();
            foreach (var hn in (System.Environment.GetEnvironmentVariable("PSX_SKYLINE_HOURS") ?? "noon,dusk,night").Split(','))
            {
                int hi = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hn.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (hi >= 0) hours.Add(hi);
            }
            var go = new GameObject("~skylinePlayWorld");
            var world = go.AddComponent<CityWorld>();
            var log = new System.Text.StringBuilder();
            try
            {
                foreach (float km in kms)
                foreach (int side in new[] { -1, 1 })
                {
                    string name = (side < 0 ? "i77_south_" : "i77_north_") + km.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "km";
                    // the I-77 carriageway point nearest km from uptown on this side
                    float best = float.MaxValue; int bei = -1; float bs = 0f;
                    for (int ei = 0; ei < map.edges.Length; ei++)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.name != "I-77") continue;
                        for (float s = 0f; s <= e.length; s += 20f)
                        {
                            var p = e.PointAt(s);
                            if (Mathf.Sign(p.y - map.uptown.y) != side) continue;
                            float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - km * 1000f);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei < 0) { Debug.LogError($"[SkylinePlay] no I-77 {name}"); continue; }
                    var be = map.edges[bei];
                    var at = be.PointAt(bs);
                    var eye = new Vector3(at.x, be.YAt(bs) + 2.2f, at.y);
                    var core = new Vector3(map.uptown.x, eye.y + 40f, map.uptown.y);
                    var rot = Quaternion.LookRotation(core - eye);
                    world.EnsureRing(eye, world.ring);
                    foreach (int hour in hours)
                    {
                        TimeOfDay.Apply(hour, sun);
                        NightGlow.PreviewAll(hour >= TimeOfDay.Dusk);
                        if (globals != null) globals.Apply();
                        cam.transform.SetPositionAndRotation(eye, rot);
                        if (on) world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, $"skyline_{name}_{TimeOfDay.All[hour].name.ToLowerInvariant()}{(on ? "" : "_off")}", eye, rot);
                    }
                    var sk = world.Skyline;
                    log.Append($"{name}: eye ({eye.x:0},{eye.y:0.0},{eye.z:0}) {Vector2.Distance(at, map.uptown):0} m from uptown, far {cam.farClipPlane:0} m, fog {(globals != null ? globals.fogNear : 0f):0}-{(globals != null ? globals.fogFar : 0f):0} m, fov {cam.fieldOfView:0}, tiles {world.LiveTiles}, skyline {(on ? "on" : "off")} {(sk != null ? sk.Shown + "/" + sk.ElementCount + " buildings, " + sk.Triangles + " tris" : "none")}\n");
                    world.DropAll();
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            Debug.Log("[SkylinePlay] " + log.ToString().Replace("\n", " | "));
        }

        static List<GameObject> BuildRing(CityMap map, CityMeshes.Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, GameObject parent, Vector2 at, int ring,
            out string stats)
        {
            var made = new List<GameObject>();
            int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
            var mats = CityMaterialsForPreview();
            int roadV = 0, bldV = 0, foot = 0, houses = 0, gores = 0, facing = 0, barrierV = 0;
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, ptx + dx, ptz + dz);
                    var root = new GameObject($"~tile_{ptx + dx}_{ptz + dz}");
                    root.transform.SetParent(parent.transform, false);
                    root.transform.position = tm.origin;
                    Wrap(root, tm.ground, mats, tm.groundSlots);
                    Wrap(root, tm.roads, mats, tm.roadSlots);
                    Wrap(root, tm.barriers, mats, new[] { CityMeshes.Slot.Concrete });
                    // leftover item 2: the W-beam lead-ins at parapet ends (the
                    // game draws them with the tile's furniture; the preview
                    // stands no poles, lamps or trees, so it draws them alone)
                    var wbm = CityPoles.WBeamMesh(tm);
                    if (wbm != null)
                    {
                        var wg = new GameObject("WBeams");
                        wg.transform.SetParent(root.transform, false);
                        wg.AddComponent<MeshFilter>().sharedMesh = wbm;
                        wg.AddComponent<MeshRenderer>().sharedMaterial = CityPoles.Material();
                    }
                    // The kerb faces are their own render-only mesh now (no
                    // collider), exactly as CityWorld stands them up.
                    Wrap(root, tm.kerbs, mats, new[] { CityMeshes.Slot.Concrete });
                    Wrap(root, tm.water, mats, new[] { CityMeshes.Slot.Water });
                    Wrap(root, tm.buildings, mats, tm.buildingSlots);
                    roadV += tm.roads != null ? tm.roads.vertexCount : 0;
                    bldV += tm.buildings != null ? tm.buildings.vertexCount : 0;
                    barrierV += tm.barriers != null ? tm.barriers.vertexCount : 0;
                    foot += tm.footprintCount; houses += tm.houseCount; gores += tm.goreCount;
                    facing += tm.wallFacingErrors;

                    // the prop lots, exactly as CityWorld stands them up
                    long key = ((long)(ptx + dx) << 24) ^ ((ptz + dz) & 0xFFFFFF);
                    if (buildings.TryGetValue(key, out var lots))
                        foreach (var b in lots)
                        {
                            if (b.kind == 0) continue;
                            var prefab = CityProps.CityPrefab(b.kind);
                            if (prefab == null) continue;
                            var def = CityProps.Defs[b.kind];
                            // the game's seat and its empty lots (leftover item 6)
                            if (!CityBuildings.PropSeat(map, b, def, out float seat, out _)) continue;
                            var inst = (GameObject)Object.Instantiate(prefab, root.transform);
                            inst.transform.position = new Vector3(b.pos.x, seat - def.sink, b.pos.y);
                            inst.transform.rotation = Quaternion.Euler(
                                0f, b.yaw * Mathf.Rad2Deg + def.yawOffsetDeg, 0f);
                            if (b.scale.sqrMagnitude > 0.01f) inst.transform.localScale = b.scale;
                        }
                    made.Add(root);
                }
            stats = $"{made.Count} tiles, {roadV} road verts, {bldV} building verts, {barrierV} barrier verts, " +
                    $"{foot} footprints, {houses} filled houses, {gores} gores, {facing} facing errors";
            return made;
        }

        static void Wrap(GameObject parent, Mesh mesh, Material[] mats, CityMeshes.Slot[] slots)
        {
            if (mesh == null) return;
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++) use[i] = mats[(int)slots[i]];
            mr.sharedMaterials = use;
        }

        /// <summary>The materials the GAME uses, not a copy of them: the city
        /// kit (WP-07), which EnsureCityTextures has just rewritten.</summary>
        static Material[] CityMaterialsForPreview()
        {
            var kit = PSXRacingBuilder.EnsureCityKit();
            return kit != null ? kit.slots : PSXRacingBuilder.CityMaterials();
        }

        static string Tag(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho, float far = 3000f,
                          int w = 960, int h = 540, float fov = 60f, float near = 0.3f)
        {
            var camGO = new GameObject("~previewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = near;
            cam.farClipPlane = far;
            cam.fieldOfView = fov;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }

            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            ShotSidecar.WritePng(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
