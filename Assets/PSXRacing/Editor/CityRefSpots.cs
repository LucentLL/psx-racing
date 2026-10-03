using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE REFERENCE SPOTS (Charlotte refinement WP-01, plan section 2.4):
    /// fixed driver-eye cameras at the places the surveys judged, shot the
    /// same way before and after every package so the owner can compare them
    /// side by side (tools/city/refsheet.py makes the contact sheet).
    ///
    ///   sv_*     the 15 Street View spots of survey_streetview.md (A1-A15,
    ///            21 points), at the capture's own heading. Google is only
    ///            LOOKED at; nothing here came from it but a place and a
    ///            compass heading to stand at.
    ///   kink_*   the 16 kink-verdict spots plus the census's worst 20
    ///            (24 places): from 40 m back down the road looking at the
    ///            kink, and straight down from 100 m (the line itself).
    ///   crest_* / dip_*  the transects' named crests and dips
    ///            (survey_flatness section 4): from 80 m back, looking at it.
    ///
    /// Each spot is snapped to the nearest edge whose name contains the
    /// spot's road (so the Independence crest is shot on Independence, not on
    /// the street crossing it), else to the nearest road. The world is built
    /// through CityWorld.EnsureTile with the game's materials, lamps included,
    /// under the preview's daylight. 640x360 PNGs to Screenshots/City/ref,
    /// plus ref_spots.txt saying where each camera stood.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityRefSpots.Run
    /// (tools\city-refspots.ps1 runs it and builds the sheet).
    /// </summary>
    public static partial class CityRefSpots
    {
        enum Kind { StreetView, Kink, Crest }

        struct Spot
        {
            public string id, road, what; public double lat, lon; public float hdg; public Kind kind;
            public Spot(string id, Kind kind, double lat, double lon, string road, float hdg, string what)
            { this.id = id; this.kind = kind; this.lat = lat; this.lon = lon; this.road = road; this.hdg = hdg; this.what = what; }
        }

        static readonly Spot[] Spots =
        {
            // ---- survey_streetview.md PART A (hdg = the capture's compass heading)
            new Spot("sv_a1_trade", Kind.StreetView, 35.22662, -80.84248, "Trade", 305f, "A1 uptown, E Trade St"),
            new Spot("sv_a1_tryon", Kind.StreetView, 35.22880, -80.84078, "Tryon", 229f, "A1 uptown, N Tryon St"),
            new Spot("sv_a2_i277", Kind.StreetView, 35.23363, -80.83582, "I-277", 131f, "A2 I-277 Brookshire Fwy"),
            new Spot("sv_a3_i77w", Kind.StreetView, 35.22700, -80.86208, "I-77", 46f, "A3 I-77 west of uptown"),
            new Spot("sv_a3_billlee", Kind.StreetView, 35.19001, -80.88627, "I-77", 21f, "A3 I-77 Bill Lee Fwy"),
            new Spot("sv_a4_i485", Kind.StreetView, 35.06068, -80.76398, "I-485", 256f, "A4 I-485 wooded stretch"),
            new Spot("sv_a5_strip", Kind.StreetView, 35.16804, -80.74309, "Independence", 334f, "A5 Independence Blvd strip"),
            new Spot("sv_a5_expwy", Kind.StreetView, 35.20095, -80.77520, "Independence", 282f, "A5 Independence Expressway"),
            new Spot("sv_a6_south", Kind.StreetView, 35.15993, -80.87621, "South Boulevard", 181f, "A6 South Blvd, Archdale"),
            new Spot("sv_a7_wilk1", Kind.StreetView, 35.22463, -80.90005, "Wilkinson", 95f, "A7 Wilkinson Blvd, industrial"),
            new Spot("sv_a7_wilk2", Kind.StreetView, 35.23296, -80.92771, "Wilkinson", 101f, "A7 Wilkinson Blvd near the airport"),
            new Spot("sv_a8_queens", Kind.StreetView, 35.19280, -80.83678, "Queens", 13f, "A8 Queens Rd W, Myers Park"),
            new Spot("sv_a8_dilworth", Kind.StreetView, 35.20225, -80.84758, "Dilworth", 192f, "A8 Dilworth Rd E"),
            new Spot("sv_a9_providence", Kind.StreetView, 35.11889, -80.77971, "Providence", 351f, "A9 Providence Rd near Alexander Rd"),
            new Spot("sv_a10_rockyriver", Kind.StreetView, 35.27495, -80.69288, "Rocky River", 243f, "A10 Rocky River Rd, two-lane"),
            new Spot("sv_a10_beatties", Kind.StreetView, 35.33571, -80.87534, "Beatties Ford", 335f, "A10 Beatties Ford Rd"),
            new Spot("sv_a11_central", Kind.StreetView, 35.22019, -80.80899, "Central", 94f, "A11 Central Ave, Plaza Midwood"),
            new Spot("sv_a12_monroe", Kind.StreetView, 35.14852, -80.74559, "Monroe", 157f, "A12 Monroe Rd over McAlpine Creek"),
            new Spot("sv_a13_brentwood", Kind.StreetView, 35.21548, -80.87690, "Brentwood", 328f, "A13 Brentwood Pl, west side"),
            new Spot("sv_a14_i85", Kind.StreetView, 35.26123, -80.87953, "I-85", 45f, "A14 I-85 in the suburbs"),
            new Spot("sv_a15_albemarle", Kind.StreetView, 35.20239, -80.72967, "Albemarle", 95f, "A15 Albemarle Rd"),
            // the roads pass (A1, 2026-10-02): the owner's own frame - W 5th St at node 2069, the west end of
            // the bridge over I-77, heading 134 (two decks, four parapets, where Street View shows one bridge)
            new Spot("sv_w5th_west", Kind.StreetView, 35.23801, -80.85467, "West 5th", 134f, "W 5th St over I-77 from node 2069, the owner's frame"),

            // ---- kinks: census worst 20 (kinks/worst20.json) + the verdict-only spots
            new Spot("kink01_beatties_gilead", Kind.Kink, 35.422512, -80.915943, "Beatties Ford", 0f, "#1 77 deg, genuine skewed junction"),
            new Spot("kink02_i77_tolldrop", Kind.Kink, 35.336365, -80.848755, "I-77", 0f, "#2 20.2 deg, toll-drop artefact"),
            new Spot("kink03_northlake", Kind.Kink, 35.353752, -80.856231, "Northlake", 0f, "#3 60.5 deg, node 0.3 m off the line"),
            new Spot("kink04_wendover_dogleg", Kind.Kink, 35.199953, -80.787853, "Wendover", 0f, "#4 40 deg dogleg, noise"),
            new Spot("kink05_southpoint", Kind.Kink, 35.179208, -81.021379, "South Point", 0f, "#5 78 deg, genuine crossroads"),
            new Spot("kink06_eastway_z", Kind.Kink, 35.204595, -80.784932, "Eastway", 0f, "#6 25 deg Z, lane-count jog"),
            new Spot("kink07_indep_join", Kind.Kink, 35.196323, -80.767574, "Independence", 0f, "#7 13.8 deg at a way join"),
            new Spot("kink08_wendover_join", Kind.Kink, 35.193386, -80.794748, "Wendover", 0f, "#8 24 deg dogleg at a way join"),
            new Spot("kink09_indep_blvd", Kind.Kink, 35.200340, -80.772829, "Independence", 0f, "#9 11.5 deg"),
            new Spot("kink10_mtholly_rbt", Kind.Kink, 35.336098, -80.913520, "Mount Holly", 0f, "#10 27 deg, roundabout entry flare"),
            new Spot("kink11_wtharris", Kind.Kink, 35.236098, -80.735161, "Harris", 0f, "#11 17.6 deg micro-zig"),
            new Spot("kink12_e13th", Kind.Kink, 35.233891, -80.833517, "13th", 0f, "#12 25.5 deg, real curve, irregular spacing"),
            new Spot("kink13_statesville", Kind.Kink, 35.320887, -80.842139, "Statesville", 0f, "#13 15 deg"),
            new Spot("kink14_providence", Kind.Kink, 35.168405, -80.804688, "Providence", 0f, "#14 14.4 deg, real curve undersampled"),
            new Spot("kink15_providence", Kind.Kink, 35.157948, -80.798112, "Providence", 0f, "#15 12.9 deg"),
            new Spot("kink16_sc51", Kind.Kink, 35.084330, -80.932475, "51", 0f, "#16 15.1 deg, real curve undersampled"),
            new Spot("kink17_wendover", Kind.Kink, 35.191373, -80.796241, "Wendover", 0f, "#17 12 deg"),
            new Spot("kink18_i77n", Kind.Kink, 35.331171, -80.847962, "I-77", 0f, "#18 12.1 deg, toll-drop artefact"),
            new Spot("kink19_plaza", Kind.Kink, 35.232101, -80.807863, "Plaza", 0f, "#19 17.5 deg, turn-lane jog"),
            new Spot("kink20_johnston", Kind.Kink, 35.084155, -80.851649, "Johnston", 0f, "#20 17.4 deg"),
            new Spot("kink_eastway_fold", Kind.Kink, 35.21200, -80.78162, "Eastway", 0f, "53 deg, 2.8 m fold at a carriageway split"),
            new Spot("kink22_brookshire", Kind.Kink, 35.22206, -80.82867, "I-277", 0f, "#22 10.6 deg, real curve undersampled"),
            new Spot("kink_mcdowell_jog", Kind.Kink, 35.21571, -80.84159, "McDowell", 0f, "2.26 m jog under I-277"),
            new Spot("kink37_statesville", Kind.Kink, 35.410981, -80.855341, "Statesville", 0f, "#37 20.4 deg, road being widened"),

            // ---- survey_flatness section 4: named crests and dips (real height in the label)
            new Spot("crest_indep_pecan", Kind.Crest, 35.2177, -80.8140, "Independence", 0f, "Independence crest by Pecan Ave, 22.6 m real"),
            new Spot("dip_indep_briar", Kind.Crest, 35.2109, -80.8014, "Independence", 0f, "Independence dip by Briar Ck Rd, 31.7 m real"),
            new Spot("dip_indep_east", Kind.Crest, 35.2062, -80.7955, "Independence", 0f, "Independence dip, 4.6 m real"),
            new Spot("dip_i77_w5th", Kind.Crest, 35.2384, -80.8535, "I-77", 0f, "I-77 dip by W 5th St, 11.2 m real"),
            new Spot("crest_i77", Kind.Crest, 35.2319, -80.8594, "I-77", 0f, "I-77 crest, 11.2 m real"),
            new Spot("crest_tryon_dalton", Kind.Crest, 35.2391, -80.8273, "Tryon", 0f, "N Tryon crest at Dalton Ave, 10.0 m real"),
            new Spot("crest_providence_laurel", Kind.Crest, 35.2014, -80.8251, "Providence", 0f, "Providence Rd crest by S Laurel Ave, 7.9 m real"),
            new Spot("dip_providence", Kind.Crest, 35.2063, -80.8241, "Providence", 0f, "Providence Rd dip, 5.0 m real"),
            new Spot("dip_freedom_morehead", Kind.Crest, 35.2274, -80.8670, "Freedom", 0f, "Freedom Dr dip by W Morehead, 10.6 m real"),
            new Spot("crest_i485_park", Kind.Crest, 35.0872, -80.8743, "I-485", 0f, "I-485 crest near Park Rd, 15.7 m real"),
            new Spot("dip_tyvola_park", Kind.Crest, 35.1553, -80.8514, "Tyvola", 0f, "Tyvola dip by Park Rd, 15.9 m real"),
        };

        const float EyeM = 1.2f, FovDeg = 58f, FarM = 500f;
        const int W = 640, H = 360;

        static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        /// <summary>Nearest point on an edge whose name contains
        /// <paramref name="road"/> (case-insensitive) within 150 m, else on
        /// any road within 400 m.</summary>
        static bool Snap(CityMap map, Vector2 p, string road, out CityMap.Edge edge, out float s, out bool named)
        {
            edge = null; s = 0f; named = false;
            float best = float.MaxValue;
            if (!string.IsNullOrEmpty(road))
                foreach (var e in map.edges)
                {
                    if (e.name == null || e.name.IndexOf(road, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // cheap reject on the end points' box
                    Vector2 a = e.pts[0], b = e.pts[e.pts.Length - 1];
                    if (Mathf.Min(a.x, b.x) - p.x > 150f + e.length || p.x - Mathf.Max(a.x, b.x) > 150f + e.length) continue;
                    for (int i = 0; i + 1 < e.pts.Length; i++)
                    {
                        Vector2 q0 = e.pts[i], d = e.pts[i + 1] - q0;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, d) / L2) : 0f;
                        float dd = Vector2.Distance(p, q0 + d * t);
                        if (dd < best) { best = dd; edge = e; s = e.s[i] + Mathf.Sqrt(L2) * t; }
                    }
                }
            if (edge != null && best <= 150f) { named = true; return true; }
            if (map.NearestRoadPoint(p, 400f, false, out int ei, out float s2, out _)) { edge = map.edges[ei]; s = s2; return true; }
            return false;
        }

        [MenuItem("PSX Racing/Preview Charlotte Reference Spots")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "ref");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);

            PSXRacingBuilder.EnsureCityTextures();
            // the preview's daylight (CityPreview.Run), so every package's
            // shots are lit alike
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var go = new GameObject("~cityRefSpots");
            // no materials handed in: the world reads the city kit, as the game does
            var world = go.AddComponent<CityWorld>();
            var log = new StringBuilder("id\tkind\tsnapped_to\tnamed\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\twhat\n");
            int shots = 0;
            try
            {
                foreach (var sp in Spots)
                {
                    var at = LL(sp.lat, sp.lon);
                    if (!Snap(map, at, sp.road, out var e, out float s, out bool named))
                    { Debug.LogWarning($"[CityRefSpots] {sp.id}: no road near"); continue; }
                    var p = e.PointAt(s);
                    var tan = e.TangentAt(s);
                    float y = e.YAt(s);
                    world.EnsureRing(new Vector3(p.x, 0f, p.y), 1);

                    Vector3 eye, look;
                    if (sp.kind == Kind.StreetView)
                    {
                        float h = sp.hdg * Mathf.Deg2Rad;
                        var d = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
                        eye = new Vector3(p.x, y + EyeM, p.y);
                        look = eye + d * 50f + Vector3.down * 1.0f;
                    }
                    else
                    {
                        float back = sp.kind == Kind.Kink ? 40f : 80f;
                        // along the road towards the spot, from whichever side
                        // the road runs on (the tangent's own direction)
                        var e2 = p - tan * back;
                        float ey = y;
                        if (map.NearestRoadPoint(e2, 30f, false, out int ei2, out float s2, out _)) ey = map.edges[ei2].YAt(s2);
                        eye = new Vector3(e2.x, ey + EyeM, e2.y);
                        look = new Vector3(p.x, y + 1.0f, p.y);
                    }
                    // the ring around the eye as well, when it stands in the next tile
                    world.EnsureRing(eye, 1);
                    Shoot(dir, sp.id, eye, Quaternion.LookRotation(look - eye), 0f);
                    shots++;
                    if (sp.kind == Kind.Kink)
                    {
                        Shoot(dir, sp.id + "_top", new Vector3(p.x, y + 100f, p.y), Quaternion.Euler(90f, Mathf.Atan2(tan.x, tan.y) * Mathf.Rad2Deg, 0f), 45f);
                        shots++;
                    }
                    log.Append($"{sp.id}\t{sp.kind}\te{e.index} '{e.name}' s={s:0}\t{(named ? "yes" : "NO")}\t{eye.x:0.0}\t{eye.y:0.00}\t{eye.z:0.0}\t{look.x:0.0}\t{look.y:0.00}\t{look.z:0.0}\t{sp.what}\n");
                    world.DropAll();
                }
            }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "ref_spots.txt"), log.ToString());
            Debug.Log($"[CityRefSpots] {shots} shots of {Spots.Length} spots to {dir}");
        }

        /// <summary>
        /// THE TREES IN EVERY DRESS (WP-08): three of the spots - Queens Road
        /// in Myers Park, Central Avenue in Plaza Midwood, the wooded I-485 -
        /// shot five times, the city's trees wearing each season's atlas in
        /// turn (winter, spring, summer, fall, snow; the ground keeps its baked
        /// fall, as it does in every edit-mode shot). To
        /// Screenshots/City/trees/&lt;spot&gt;_&lt;dress&gt;.png.
        /// Headless: -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeDresses
        /// </summary>
        public static void RunTreeDresses()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "trees");
            Directory.CreateDirectory(dir);
            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            var kit = CityKit.Get();
            var go = new GameObject("~cityTreeDresses");
            var world = go.AddComponent<CityWorld>();
            int shots = 0;
            try
            {
                foreach (var sp in Spots)
                {
                    if (sp.id != "sv_a8_queens" && sp.id != "sv_a11_central" && sp.id != "sv_a4_i485") continue;
                    if (!Snap(map, LL(sp.lat, sp.lon), sp.road, out var e, out float s, out _)) continue;
                    var p = e.PointAt(s);
                    float h = sp.hdg * Mathf.Deg2Rad;
                    var eye = new Vector3(p.x, e.YAt(s) + EyeM, p.y);
                    var look = eye + new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * 50f + Vector3.down * 1.0f;
                    world.EnsureRing(eye, 1);
                    for (int d = 0; d < Seasons.DressCount; d++)
                    {
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
                            if (r.gameObject.name == "Trees" && kit != null && kit.trees != null && d < kit.trees.Length) r.sharedMaterial = kit.trees[d];
                        Shoot(dir, sp.id + "_" + Seasons.DressNames[d].ToLowerInvariant(), eye, Quaternion.LookRotation(look - eye), 0f);
                        shots++;
                    }
                    world.DropAll();
                }
            }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[CityRefSpots] {shots} tree dress shots to {dir}");
        }

        /// <summary>
        /// THE SIGNS, BEFORE AND AFTER (WP-23): at the plan's shot roads - I-77,
        /// I-277, South Blvd - and the Independence strip, the camera stands
        /// where a sign's own driver is (<see cref="CitySigns.Face.viewer"/>,
        /// 150 m up the road at eye height) and looks at it: the nearest
        /// billboard, exit gantry and business pole sign to each spot. Three
        /// frames each: day with the signs (&lt;shot&gt;_after), the same camera
        /// with the city built with NO signs (&lt;shot&gt;_before: the WP-08
        /// city, trees and all), and the night (&lt;shot&gt;_night: the hour
        /// through TimeOfDay, lamps and halos lit). To Screenshots/City/signs,
        /// with signs_shots.txt saying where each camera stood.
        /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.CityRefSpots.RunSigns
        /// </summary>
        public static void RunSigns()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "signs");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            PSXRacingBuilder.EnsureCityTextures();
            // the plan's shot roads, the strip, and three streets the game lines with stores
            var spots = new[] { "sv_a3_i77w", "sv_a2_i277", "sv_a6_south", "sv_a5_strip", "sv_a14_i85", "sv_a11_central", "sv_a15_albemarle", "sv_a7_wilk1" };
            var cams = new List<(string name, Vector3 eye, Vector3 look, Vector2 at, string what)>();
            var log = new StringBuilder("shot	eye_x	eye_y	eye_z	look_x	look_y	look_z	what\n");
            var go = new GameObject("~citySignShots");
            var world = go.AddComponent<CityWorld>();
            bool signsWere = CitySigns.Enabled;
            GameObject sunGo = null;
            try
            {
                // ---- day, with the signs: choose the cameras and shoot
                DayLight();
                CitySigns.Enabled = true;
                foreach (var sp in Spots)
                {
                    if (System.Array.IndexOf(spots, sp.id) < 0) continue;
                    if (!Snap(map, LL(sp.lat, sp.lon), sp.road, out var e, out float s, out _)) continue;
                    var p = e.PointAt(s);
                    world.EnsureRing(new Vector3(p.x, 0f, p.y), 2);
                    foreach (var kind in new[] { CitySigns.Kind.Bulletin, CitySigns.Kind.Gantry, CitySigns.Kind.PoleSign, CitySigns.Kind.Poster })
                    {
                        CitySigns.Face best = default; float bd = 700f; bool found = false;
                        foreach (var st in world.LiveSigns)
                            foreach (var f in st.faces)
                            {
                                if (f.kind != kind) continue;
                                float d = Vector2.Distance(new Vector2(f.centre.x, f.centre.z), p);
                                if (d < bd) { bd = d; best = f; found = true; }
                            }
                        if (!found) continue;
                        // on the line from the driver it was turned to, a little
                        // higher (a car's chase eye), near enough to read it: a
                        // bulletin from 85 m, a poster 60, a cabinet 40, a gantry 110
                        var toFace = best.centre - best.viewer;
                        float keep = kind == CitySigns.Kind.Bulletin ? 85f : kind == CitySigns.Kind.Poster ? 60f : kind == CitySigns.Kind.PoleSign ? 40f : 110f;
                        var eye = best.viewer + toFace.normalized * Mathf.Max(0f, toFace.magnitude - keep) + Vector3.up * 0.8f;
                        var look = best.centre - Vector3.up * (best.h * 0.3f);
                        string name = sp.id.Replace("sv_", "") + "_" + kind.ToString().ToLowerInvariant();
                        world.EnsureRing(eye, 1);
                        cams.Add((name, eye, look, p, $"{sp.what}: the nearest {kind} ({bd:0} m from the spot)"));
                        Shoot(dir, name + "_after", eye, Quaternion.LookRotation(look - eye), 0f);
                        // and the art itself, close, square in front of the face
                        if (kind != CitySigns.Kind.Gantry)
                        {
                            var ce = best.centre + best.normal * (best.w * 1.4f) - Vector3.up * (best.h * 0.6f);
                            Shoot(dir, name + "_close", ce, Quaternion.LookRotation(best.centre - ce), 0f);
                        }
                        log.Append($"{name}\t{eye.x:0.0}\t{eye.y:0.00}\t{eye.z:0.0}\t{look.x:0.0}\t{look.y:0.00}\t{look.z:0.0}\t{sp.what}: the nearest {kind}, {bd:0} m from the spot\n");
                        if (kind == CitySigns.Kind.PoleSign)
                        {
                            // and the street it stands on, from its driver 80 m up
                            // the road: the frontage's signs, not one cabinet
                            var se = best.viewer + Vector3.up * 1.2f;
                            var sl = new Vector3(best.centre.x, best.viewer.y + 1.5f, best.centre.z);
                            string sn = sp.id.Replace("sv_", "") + "_street";
                            world.EnsureRing(se, 1);
                            cams.Add((sn, se, sl, p, $"{sp.what}: the street of the nearest pole sign, from its driver"));
                            Shoot(dir, sn + "_after", se, Quaternion.LookRotation(sl - se), 0f);
                            log.Append($"{sn}\t{se.x:0.0}\t{se.y:0.00}\t{se.z:0.0}\t{sl.x:0.0}\t{sl.y:0.00}\t{sl.z:0.0}\t{sp.what}: the street of the nearest pole sign, from its driver\n");
                        }
                    }
                    world.DropAll();
                }
                // ---- the same cameras, no signs (the city before WP-23)
                CitySigns.Enabled = false;
                foreach (var c in cams)
                {
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 2);
                    world.EnsureRing(c.eye, 1);
                    Shoot(dir, c.name + "_before", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    world.DropAll();
                }
                // ---- night, with the signs: the hour through TimeOfDay, as the
                // night-look shots do it (edit mode runs no Awake: the lamps are
                // lit by hand)
                CitySigns.Enabled = true;
                sunGo = new GameObject("~signSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                var globals = sunGo.AddComponent<PSXGlobals>();
                globals.sun = sun;
                TimeOfDay.Apply(TimeOfDay.Night, sun);
                globals.Apply();
                nightSky = globals.fogColor;
                foreach (var c in cams)
                {
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 2);
                    world.EnsureRing(c.eye, 1);
                    NightGlow.PreviewAll(true);
                    globals.Apply();
                    Shoot(dir, c.name + "_night", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    world.DropAll();
                }
            }
            finally
            {
                nightSky = null;
                CitySigns.Enabled = signsWere;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
            }
            File.WriteAllText(Path.Combine(dir, "signs_shots.txt"), log.ToString());
            Debug.Log($"[CityRefSpots] {cams.Count} sign cameras, {cams.Count * 3} shots to {dir}");
        }

        /// <summary>
        /// THE POLES, BEFORE AND AFTER (WP-15): the driver's eye in the lane at
        /// the plan's shot roads - two arterials (Albemarle Rd, Central Ave in
        /// Plaza Midwood), two two-lane streets (Rocky River Rd, Brentwood Pl
        /// on the west side), uptown (E Trade St, N Tryon St) and Queens Rd in
        /// Myers Park (no wires there) - looking down the road at the Street
        /// View capture's heading, level, by day and at night (the hour
        /// through TimeOfDay, the lamps lit by hand as the sign shots do it).
        /// The same cameras every run; PSX_POLE_LABEL names the run (before /
        /// after), so a run on the tree before the package and one after it
        /// make the pairs. To Screenshots/City/poles/&lt;spot&gt;_&lt;label&gt;_{day,night}.png.
        /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.CityRefSpots.RunPoles
        /// </summary>
        public static void RunPoles()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string label = System.Environment.GetEnvironmentVariable("PSX_POLE_LABEL");
            if (string.IsNullOrEmpty(label)) label = "now";
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "poles");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*_" + label + "_*.png")) File.Delete(f);
            PSXRacingBuilder.EnsureCityTextures();
            var spots = new[] { "sv_a15_albemarle", "sv_a11_central", "sv_a10_rockyriver", "sv_a13_brentwood", "sv_a1_trade", "sv_a1_tryon", "sv_a8_queens" };
            // PSX_POLE_EXTRA="name,lat,lon,road,hdg;...": cameras of one's own
            // beside the plan's (a junction a fix was made at), shot the same way
            var all = new List<Spot>(Spots);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var part in (System.Environment.GetEnvironmentVariable("PSX_POLE_EXTRA") ?? "").Split(';'))
            {
                var f = part.Split(',');
                if (f.Length == 5 && double.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out double la)
                                  && double.TryParse(f[2], System.Globalization.NumberStyles.Float, inv, out double lo)
                                  && float.TryParse(f[4], System.Globalization.NumberStyles.Float, inv, out float hd))
                {
                    all.Add(new Spot("sv_" + f[0].Trim(), Kind.StreetView, la, lo, f[3].Trim(), hd, f[0].Trim()));
                    System.Array.Resize(ref spots, spots.Length + 1);
                    spots[spots.Length - 1] = "sv_" + f[0].Trim();
                }
            }
            // PSX_POLE_SPOTS=a15_albemarle,a1_tryon: only those cameras (a lean run)
            string only = System.Environment.GetEnvironmentVariable("PSX_POLE_SPOTS");
            if (!string.IsNullOrEmpty(only))
            {
                var want = new HashSet<string>(only.Split(','));
                spots = System.Array.FindAll(spots, id => want.Contains(id) || want.Contains(id.Replace("sv_", "")));
            }
            var cams = new List<(string name, Vector3 eye, Vector3 look, Vector2 at)>();
            var log = new StringBuilder("shot\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\twhat\n");
            var go = new GameObject("~cityPoleShots");
            var world = go.AddComponent<CityWorld>();
            GameObject sunGo = null;
            try
            {
                DayLight();
                foreach (var sp in all)
                {
                    if (System.Array.IndexOf(spots, sp.id) < 0) continue;
                    if (!Snap(map, LL(sp.lat, sp.lon), sp.road, out var e, out float s, out _)) continue;
                    var p = e.PointAt(s);
                    float h = sp.hdg * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Sin(h), Mathf.Cos(h));
                    // in the lane: half the carriageway's half width to the right
                    // of travel on a two-way road, the middle of a one-way one
                    var right = new Vector2(d.y, -d.x);
                    var q = p + (e.oneway ? Vector2.zero : right * (e.PaveEdgeM(s, 1) * 0.5f));
                    var eye = new Vector3(q.x, e.YAt(s) + EyeM, q.y);
                    var look = eye + new Vector3(d.x, 0f, d.y) * 60f + Vector3.up * 1.5f;
                    string name = sp.id.Replace("sv_", "");
                    cams.Add((name, eye, look, p));
                    log.Append($"{name}\t{eye.x:0.0}\t{eye.y:0.00}\t{eye.z:0.0}\t{look.x:0.0}\t{look.y:0.00}\t{look.z:0.0}\t{sp.what}: e{e.index} '{e.name}' cls{e.cls} s={s:0}\n");
                }
                foreach (var c in cams)
                {
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 1);
                    world.EnsureRing(c.eye, 1);
                    Shoot(dir, c.name + "_" + label + "_day", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    // and the nearest pole ahead, close: its crossarm, cobra-head
                    // and wires from the road's edge 14 m before it
                    CityPoles.Pole best = default; float bd = 120f; bool found = false;
                    var fwd = new Vector2(c.look.x - c.eye.x, c.look.z - c.eye.z).normalized;
                    foreach (var pt in world.LivePoles)
                        foreach (var p in pt.poles)
                        {
                            var to = new Vector2(p.foot.x - c.eye.x, p.foot.z - c.eye.z);
                            float ahead = Vector2.Dot(to, fwd);
                            if (ahead < 15f || to.magnitude > bd) continue;
                            bd = to.magnitude; best = p; found = true;
                        }
                    if (found)
                    {
                        var foot = best.foot;
                        var road = new Vector3(foot.x, best.roadY, foot.z) - new Vector3(best.outward.x, 0f, best.outward.y) * 6f;
                        var ce = road - new Vector3(best.along.x, 0f, best.along.y) * 14f + Vector3.up * 1.6f;
                        var cl = new Vector3(foot.x, best.top - 3f, foot.z);
                        world.EnsureRing(ce, 1);
                        Shoot(dir, c.name + "_" + label + "_pole", ce, Quaternion.LookRotation(cl - ce), 0f);
                    }
                    world.DropAll();
                }
                sunGo = new GameObject("~poleSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                var globals = sunGo.AddComponent<PSXGlobals>();
                globals.sun = sun;
                TimeOfDay.Apply(TimeOfDay.Night, sun);
                globals.Apply();
                nightSky = globals.fogColor;
                foreach (var c in cams)
                {
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 1);
                    world.EnsureRing(c.eye, 1);
                    NightGlow.PreviewAll(true);
                    globals.Apply();
                    Shoot(dir, c.name + "_" + label + "_night", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    SpotFeet(world, map, c.name, c.eye, log);
                    world.DropAll();
                }
            }
            finally
            {
                nightSky = null;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
            }
            File.WriteAllText(Path.Combine(dir, "poles_shots_" + label + ".txt"), log.ToString());
            Debug.Log($"[CityRefSpots] {cams.Count} pole cameras, {cams.Count * 2} shots ({label}) to {dir}");
        }

        /// <summary>How far round a pole shot's camera its feet are checked.</summary>
        const float SpotFeetM = 120f;

        /// <summary>
        /// THE FEET AT A SHOT SPOT, CLEAR OF THE LANES AND DRIVEWAYS (the
        /// spot's own look, not the city-wide pole audit): every utility pole
        /// within <see cref="SpotFeetM"/> of the camera on no cell of the
        /// static roadside mask (pavement, clear zone, sight triangle, corner
        /// spot, building, lot or driveway, water, deck) and outside every
        /// road's keep-out, as the pole audit asks; every lamp post (uptown's
        /// acorns) off every carriageway and out of every real building, its
        /// tile built as the game builds it. A line to the shots' log (CLEAR
        /// or NOT CLEAR) and to the Unity log.
        /// </summary>
        static void SpotFeet(CityWorld world, CityMap map, string name, Vector3 eye3, StringBuilder log)
        {
            var trims = world.NodeTrims;
            var bld = world.Buildings;
            var eye = new Vector2(eye3.x, eye3.z);
            float ts = CityMeshes.TileSize;
            int poles = 0, polesBad = 0, lamps = 0, lampsBad = 0, lampsLot = 0, acorns = 0, breakaway = 0;
            float polePave = float.MaxValue, lampPave = float.MaxValue;
            var bad = new List<string>();
            float PaveAt(Vector2 f) =>
                RoadsideOccupancy.Static(map, trims, bld, Mathf.FloorToInt(f.x / ts), Mathf.FloorToInt(f.y / ts)).RoadEdgeDistance(f, out _, out _);
            string Bits(byte b)
            {
                var names = new List<string>();
                for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) names.Add(RoadsideOccupancy.BitNames[i]);
                return string.Join("+", names);
            }
            foreach (var pt in world.LivePoles)
                foreach (var p in pt.poles)
                {
                    var f = new Vector2(p.foot.x, p.foot.z);
                    if (Vector2.Distance(f, eye) > SpotFeetM) continue;
                    poles++;
                    float pave = PaveAt(f);
                    polePave = Mathf.Min(polePave, pave);
                    byte sb = RoadsideOccupancy.StaticAt(map, trims, bld, f);
                    float w = CitySigns.WorstRoad(map, trims, f, out _, out string what);
                    if (sb == 0 && w >= -0.05f && pave >= 0f) continue;
                    polesBad++;
                    bad.Add($"pole ({f.x:0.0},{f.y:0.0}) {pave:0.00} m off the carriageway, mask [{Bits(sb)}], keep-out {w:0.00} m ({what})");
                }
            int tx0 = Mathf.FloorToInt((eye.x - SpotFeetM) / ts), tx1 = Mathf.FloorToInt((eye.x + SpotFeetM) / ts);
            int tz0 = Mathf.FloorToInt((eye.y - SpotFeetM) / ts), tz1 = Mathf.FloorToInt((eye.y + SpotFeetM) / ts);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var tm = CityMeshes.Build(map, trims, bld, tx, tz);
                    CityMeshes.TakeLattice();
                    try
                    {
                        foreach (var l in tm.lamps)
                        {
                            var f3 = tm.origin + l.foot;
                            var f = new Vector2(f3.x, f3.z);
                            if (Vector2.Distance(f, eye) > SpotFeetM) continue;
                            lamps++;
                            if (l.kind == CityMeshes.LampAcorn) acorns++;
                            if (l.breakaway) breakaway++;
                            float pave = PaveAt(f);
                            lampPave = Mathf.Min(lampPave, pave);
                            // a lot's or driveway's cell of the mask (padded a cell: not a check)
                            if ((RoadsideOccupancy.StaticAt(map, trims, bld, f) & (RoadsideOccupancy.Building | RoadsideOccupancy.Other)) != 0) lampsLot++;
                            bool inBuilding = !map.FootprintClear(f, 0.2f);
                            if (pave >= 0f && !inBuilding) continue;
                            lampsBad++;
                            bad.Add($"lamp kind {l.kind} ({f.x:0.0},{f.y:0.0}) {pave:0.00} m off the carriageway{(inBuilding ? ", in a building" : "")}");
                        }
                    }
                    finally
                    {
                        foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.buildings, tm.lampPosts })
                            if (m != null) Object.DestroyImmediate(m);
                    }
                }
            string verdict = polesBad + lampsBad == 0 ? "CLEAR" : "NOT CLEAR";
            string line = $"feet at {name}: {poles} poles within {SpotFeetM:0} m, {polesBad} on a mask cell or in a keep-out, nearest {(poles > 0 ? polePave.ToString("0.00") : "-")} m off a carriageway; " +
                          $"{lamps} lamp posts ({acorns} acorn, {breakaway} breaking away), {lampsBad} on a carriageway or in a building, nearest {(lamps > 0 ? lampPave.ToString("0.00") : "-")} m off one, " +
                          $"{lampsLot} on a padded lot cell (not a check) -> {verdict}";
            log.Append("# " + line + "\n");
            for (int i = 0; i < Mathf.Min(10, bad.Count); i++) log.Append("#   " + bad[i] + "\n");
            Debug.Log("[CityRefSpots] " + line);
        }

        const int WireSignCams = 3;
        const float WireSignBackM = 22f;

        /// <summary>
        /// THE SIGNS AND THE WIRES (the WP-15 review), one job, two worlds
        /// from the same cameras. First the signs as the reviewed build placed
        /// them (<see cref="CitySigns.KeepOffPoles"/> off): along the city race
        /// routes, the Tryon Sprint first (the review found the telecom cables
        /// through a burger sign out of NoDa), the first business signs within
        /// 60 m of a route that a drawn wire or pole part runs through (the
        /// sign audit's 3D test), each from its own driver
        /// <see cref="WireSignBackM"/> before it, looking at the cabinet, by
        /// day (the log names the near misses too). Then the signs as they stand now (keeping off the poles), from
        /// the same cameras. To Screenshots/City/poles/wiresign_&lt;n&gt;_{before,after}.png
        /// and wiresign.txt.
        /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.CityRefSpots.RunWireSigns
        /// </summary>
        public static void RunWireSigns()
        {
            var map = CityMap.Get();
            if (map == null || map.routes == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "poles");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "wiresign_*")) File.Delete(f);
            PSXRacingBuilder.EnsureCityTextures();
            // the Tryon Sprint first (the review's find), then the other race routes
            var routes = new List<CityMap.Route>();
            foreach (var r in map.routes) if (r.id == "tryon") routes.Add(r);
            foreach (var r in map.routes) if (r.id != "tryon") routes.Add(r);
            var routeDir = new Dictionary<int, int>();
            foreach (var route in routes)
                for (int i = 0; i < route.edges.Length; i++)
                    routeDir[route.edges[i]] = route.dirs != null && i < route.dirs.Length && route.dirs[i] < 0 ? -1 : 1;
            var cams = new List<(Vector3 eye, Vector3 look, Vector2 at, string what)>();
            var log = new StringBuilder("shot\teye_x\teye_y\teye_z\tlook_x\tlook_y\tlook_z\twhat\n");
            var go = new GameObject("~cityWireSigns");
            var world = go.AddComponent<CityWorld>();
            bool keepWas = CitySigns.KeepOffPoles;
            try
            {
                DayLight();
                // BEFORE: the signs as the reviewed build placed them
                CitySigns.KeepOffPoles = false;
                var boxes = new List<CityAudit.SignBox>();
                var caps = new List<(Vector3 a, Vector3 b, float r, bool wire)>();
                var wires = new List<(Vector3 from, Vector3 to, float sag, float halfW)>();
                var parts = new List<(Vector3 a, Vector3 b, float r)>();
                var seen = new HashSet<long>();
                var found = new List<(CitySigns.Sign sg, Vector3 centre, string what)>();
                int steps = 0, examined = 0, beside = 0;
                var closest = new List<(float air, string what)>();
                // every candidate: (tryon first, a 3D hit before a plan crossing, the plan air), its camera
                var cands = new List<(int rank, float air, Vector3 eye, Vector3 look, Vector2 at, string what)>();
                foreach (var route in routes)
                for (int i = 0; i < route.edges.Length; i++)
                {
                    var e = map.edges[route.edges[i]];
                    for (float s = 0f; s < e.length + 60f; s += 120f)
                    {
                        float sa = routeDir[e.index] > 0 ? Mathf.Min(s, e.length) : Mathf.Max(0f, e.length - s);
                        var p = e.PointAt(sa);
                        world.EnsureRing(new Vector3(p.x, 0f, p.y), 1);
                        caps.Clear();
                        foreach (var pt in world.LivePoles)
                        {
                            foreach (var pole in pt.poles)
                            {
                                parts.Clear();
                                CityPoles.PartsOf(pole, parts);
                                foreach (var (a, b, r) in parts) caps.Add((a, b, r, false));
                            }
                            foreach (var (pa, pb) in pt.spans)
                            {
                                wires.Clear();
                                CityPoles.WiresOf(pa, pb, wires);
                                foreach (var w in wires)
                                    for (int k = 0; k < CityPoles.WireSegmentsDrawn; k++)
                                        caps.Add((CityPoles.WirePoint(w.from, w.to, w.sag, k / (float)CityPoles.WireSegmentsDrawn),
                                                  CityPoles.WirePoint(w.from, w.to, w.sag, (k + 1) / (float)CityPoles.WireSegmentsDrawn), w.halfW, true));
                            }
                        }
                        steps++;
                        foreach (var st in world.LiveSigns)
                            foreach (var sg in st.signs)
                            {
                                if (sg.kind != CitySigns.Kind.PoleSign || sg.faces <= 0) continue;
                                if (Vector2.Distance(sg.pos, p) > 140f) continue;
                                long key = ((long)Mathf.RoundToInt(sg.pos.x) << 32) ^ (uint)Mathf.RoundToInt(sg.pos.y);
                                if (seen.Contains(key)) continue;
                                seen.Add(key);
                                examined++;
                                // beside the race (its own road within 60 m of the route's)
                                if (!routeDir.ContainsKey(sg.edge) && !NearRoute(map, routeDir, sg.pos, 60f)) continue;
                                beside++;
                                boxes.Clear();
                                CityAudit.AddSignBoxes(st, sg, boxes);
                                string hit = null;
                                foreach (var bx in boxes)
                                {
                                    foreach (var c in caps)
                                        if (CityAudit.CapsuleInBox(c.a, c.b, c.r + CityAudit.SignWireAirM, bx)) { hit = (c.wire ? "a wire through " : "a pole part in ") + bx.what; break; }
                                    if (hit != null) break;
                                }
                                // how close the wires come in plan (the log's look at the near misses)
                                float air = float.MaxValue;
                                {
                                    var cb = boxes[0];
                                    var ca = new Vector2(cb.c.x, cb.c.z) - new Vector2(cb.x.x, cb.x.z) * cb.hx;
                                    var cz = new Vector2(cb.c.x, cb.c.z) + new Vector2(cb.x.x, cb.x.z) * cb.hx;
                                    foreach (var c in caps)
                                        if (c.wire) air = Mathf.Min(air, RoadsideOccupancy.SegSegDistance(new Vector2(c.a.x, c.a.z), new Vector2(c.b.x, c.b.z), ca, cz) - cb.hz);
                                    if (hit == null && air < 3f) closest.Add((air, $"{route.id}: cabinet at ({sg.pos.x:0},{sg.pos.y:0}) {air:0.00} m in plan from a wire, y {cb.c.y - cb.hy:0.0}-{cb.c.y + cb.hy:0.0}"));
                                }
                                // a wire through it, or crossing its footprint in plan (over it)
                                if (hit == null && air >= 0f) continue;
                                if (hit == null) hit = "a wire over (crossing in plan) a business cabinet";
                                // the camera: in the lane of the sign's own road WireSignBackM
                                // before it, on the side its front face's driver comes from
                                var fc = st.faces[sg.firstFace];
                                var se = map.edges[sg.edge];
                                float sS = NearestS(se, sg.pos);
                                int toward = Vector2.Dot(new Vector2(fc.viewer.x, fc.viewer.z) - se.PointAt(sS), se.TangentAt(sS)) >= 0f ? 1 : -1;
                                float sEye = Mathf.Clamp(sS + toward * WireSignBackM, 0f, se.length);
                                var travel = -se.TangentAt(sEye) * toward;
                                var laneRight = new Vector2(travel.y, -travel.x);
                                var q = se.PointAt(sEye) + (se.oneway ? Vector2.zero : laneRight * (se.PaveEdgeM(sEye, -toward) * 0.5f));
                                var eye = new Vector3(q.x, se.YAt(sEye) + EyeM, q.y);
                                found.Add((sg, fc.centre, hit));
                                int rank = hit.StartsWith("a wire over") ? (route.id == "tryon" ? 1 : 2) : 0;
                                cands.Add((rank, air, eye, fc.centre, sg.pos, $"{route.id}: {hit}: e{se.index} '{se.name}' at ({sg.pos.x:0.0},{sg.pos.y:0.0})"));
                            }
                        world.DropAll();
                    }
                }
                log.Append($"# walked {steps} points of the race routes (tryon first); {examined} business signs near them, {beside} beside them, {found.Count} with a wire through, over or within {CityAudit.SignWireAirM:0.0} m\n");
                // the 3D hits first, and the Tryon Sprint's nearest crossing (the review's road)
                cands.Sort((x, y) => x.rank != y.rank ? x.rank.CompareTo(y.rank) : x.air.CompareTo(y.air));
                int tryonAt = cands.FindIndex(x => x.rank == 1);
                var pick = new List<int>();
                for (int i = 0; i < cands.Count && pick.Count < WireSignCams - (tryonAt >= 0 ? 1 : 0); i++) if (i != tryonAt) pick.Add(i);
                if (tryonAt >= 0) pick.Add(tryonAt);
                foreach (int i in pick) cams.Add((cands[i].eye, cands[i].look, cands[i].at, cands[i].what));
                closest.Sort((x, y) => x.air.CompareTo(y.air));
                for (int i = 0; i < Mathf.Min(8, closest.Count); i++) log.Append("# near miss: " + closest[i].what + "\n");
                for (int i = 0; i < cams.Count; i++)
                {
                    var c = cams[i];
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 1);
                    world.EnsureRing(c.eye, 1);
                    Shoot(dir, $"wiresign_{i + 1}_before", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    world.DropAll();
                    log.Append($"wiresign_{i + 1}\t{c.eye.x:0.0}\t{c.eye.y:0.00}\t{c.eye.z:0.0}\t{c.look.x:0.0}\t{c.look.y:0.00}\t{c.look.z:0.0}\t{c.what}\n");
                }
                // AFTER: the signs as they stand now, from the same cameras
                CitySigns.KeepOffPoles = true;
                for (int i = 0; i < cams.Count; i++)
                {
                    var c = cams[i];
                    world.EnsureRing(new Vector3(c.at.x, 0f, c.at.y), 1);
                    world.EnsureRing(c.eye, 1);
                    Shoot(dir, $"wiresign_{i + 1}_after", c.eye, Quaternion.LookRotation(c.look - c.eye), 0f);
                    world.DropAll();
                }
            }
            finally
            {
                CitySigns.KeepOffPoles = keepWas;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "wiresign.txt"), log.ToString());
            Debug.Log($"[CityRefSpots] {cams.Count} wire-sign cameras, {cams.Count * 2} shots to {dir}");
        }

        /// <summary>The arc of an edge nearest a point.</summary>
        static float NearestS(CityMap.Edge e, Vector2 p)
        {
            float best = float.MaxValue, bs = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dd = Vector2.Distance(p, a + d * t);
                if (dd < best) { best = dd; bs = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
            return bs;
        }

        /// <summary>Is a point within r of any of a route's edges?</summary>
        static bool NearRoute(CityMap map, Dictionary<int, int> routeEdges, Vector2 p, float r)
        {
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, segs);
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                if (!routeEdges.ContainsKey(ei)) continue;
                var e = map.edges[ei];
                if (RoadsideOccupancy.DistToSeg(p, e.pts[si], e.pts[si + 1]) < r) return true;
            }
            return false;
        }

        /// <summary>The background the sign shots' night frames clear to (the
        /// hour's fog); null by day.</summary>
        static Color? nightSky;

        /// <summary>The preview's daylight (CityPreview.Run), so every package's
        /// shots are lit alike.</summary>
        static void DayLight()
        {
            nightSky = null;
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            Shader.SetGlobalFloat("_PSXNight", 0f);
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho)
        {
            var camGO = new GameObject("~refCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = nightSky ?? new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = ortho > 0f ? 400f : FarM;
            cam.fieldOfView = FovDeg;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
