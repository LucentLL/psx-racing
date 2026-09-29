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
    public static class CityRefSpots
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

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho)
        {
            var camGO = new GameObject("~refCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
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
