using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROADSIDE SHOTS (WP-14, plan L744): before/after views where the
    /// grading has something to grade - the I-485 south cut, an I-77 fill, the
    /// I-277 (Brookshire Freeway) fill, Providence Road at its creek - and
    /// three streets across a hillside (the land rising on one side and
    /// falling on the other), the owner's "road on a flat strip".
    ///
    /// The spots are FOUND in the data (the deepest cut / fill on the named
    /// road, from the DEM beside it, with no other road on that side) and
    /// written to a spots file; a run given that file (PSX_RS_SPOTS, when it
    /// exists) re-reads the same edges and arc positions instead, so the
    /// before and after runs stand the camera on the same road, each at its
    /// own build's road height.
    ///
    /// Three frames a spot: SEAT (driver's eye, 35 m back, in the lane on the
    /// graded side, looking down the road), HIGH (out over the graded side,
    /// 30 m past the edge and 40 m back, 6 m over the land or 7 m over the
    /// road, looking back at the road: the bank from outside), and HIGH
    /// again with the city trees off (BARE), so the land's shape is not
    /// behind a canopy. Plus roadside_shots.txt: for each spot the road, the
    /// natural land (DEM) and the graded ground 5-40 m out on both sides.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityRoadsideShots.Run
    /// (PSX_RS_OUT the output folder, PSX_RS_SPOTS the spots file).
    /// </summary>
    public static class CityRoadsideShots
    {
        struct Spot { public string id, what; public int e; public float s; public int side; }

        const float EyeM = 1.2f, FovDeg = 58f, FarM = 500f;
        const int W = 1280, H = 720;
        static readonly float[] Out = { 5f, 10f, 15f, 20f, 30f, 40f };

        static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        static bool NameHas(string name, string part) =>
            !string.IsNullOrEmpty(name) && name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        static Vector2 Normal(CityMap.Edge e, float s, int side) { var t = e.TangentAt(s); return new Vector2(-t.y, t.x) * side; }
        static float EdgeM(CityMap.Edge e, float s, int side) => e.PaveEdgeM(s, e.SideAt(s, e.PointAt(s) + Normal(e, s, side) * 5f));

        /// <summary>The DEM against the road <paramref name="o"/> metres past
        /// the pavement on a side.</summary>
        static float DemOff(CityMap.Edge e, float s, int side, float o)
        {
            var q = e.PointAt(s) + Normal(e, s, side) * (EdgeM(e, s, side) + o);
            return CityElevation.BaseY(q.x, q.y) - e.YAt(s);
        }

        /// <summary>No other road's pavement within 25 m past this side's
        /// edge, and no structure within 40 m along: the land here is this
        /// road's to grade.</summary>
        static bool Clear(CityMap map, CityMap.Edge e, float s, int side)
        {
            for (float ds = -40f; ds <= 40f; ds += 10f)
                if (e.ElevatedAt(Mathf.Clamp(s + ds, 0f, e.length))) return false;
            float hw = EdgeM(e, s, side);
            var n = Normal(e, s, side); var c = e.PointAt(s);
            for (float o = 4f; o <= 25f; o += 7f)
            {
                var q = c + n * (hw + o);
                if (map.NearestRoadPoint(q, 12f, false, out int oe, out float oat, out float od) && oe != e.index &&
                    od < map.edges[oe].PaveEdgeM(oat, map.edges[oe].SideAt(oat, q)) + 2f) return false;
            }
            return true;
        }

        delegate float Score(CityMap.Edge e, float s, int side);
        static readonly HashSet<int> waterNear = new HashSet<int>();

        static bool Best(CityMap map, System.Func<CityMap.Edge, bool> take, Score score, List<Spot> have, float apartM, out Spot spot, out float best)
        {
            spot = default; best = float.MinValue; bool ok = false;
            foreach (var e in map.edges)
            {
                if (e.link || e.length < 120f || !take(e)) continue;
                for (float s = 50f; s <= e.length - 50f; s += 10f)
                {
                    if (e.ElevatedAt(s)) continue;
                    var p = e.PointAt(s);
                    bool near = false;
                    foreach (var h in have) if (Vector2.Distance(map.edges[h.e].PointAt(h.s), p) < apartM) { near = true; break; }
                    if (near) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float sc = score(e, s, side);
                        if (sc <= best || !Clear(map, e, s, side)) continue;
                        best = sc; spot = new Spot { e = e.index, s = s, side = side }; ok = true;
                    }
                }
            }
            return ok;
        }

        static List<Spot> Find(CityMap map, StringBuilder log)
        {
            var spots = new List<Spot>();
            var up = map.uptown;
            void Add(string id, string what, bool ok, Spot sp, float sc)
            {
                if (!ok) { log.AppendLine($"{id}: NOT FOUND ({what})"); return; }
                sp.id = id; sp.what = what; spots.Add(sp);
                log.AppendLine($"{id}: e{sp.e} '{map.edges[sp.e].name}' s={sp.s:0} side {sp.side} score {sc:0.0} ({what})");
            }
            // a CUT: the land both 12 and 20 m out stands above the road
            Score cut = (e, s, side) => Mathf.Min(DemOff(e, s, side, 12f), DemOff(e, s, side, 20f));
            // a FILL: the land both 12 and 20 m out lies below it
            Score fill = (e, s, side) => -Mathf.Max(DemOff(e, s, side, 12f), DemOff(e, s, side, 20f));
            bool ok; Spot sp; float sc;
            ok = Best(map, e => NameHas(e.name, "I-485") && e.PointAt(e.length * 0.5f).y < up.y - 6000f, cut, spots, 0f, out sp, out sc);
            Add("i485_south_cut", "I-485 south of the city, its deepest cut", ok, sp, sc);
            ok = Best(map, e => NameHas(e.name, "I-77"), fill, spots, 0f, out sp, out sc);
            Add("i77_fill", "I-77, its deepest fill", ok, sp, sc);
            ok = Best(map, e => NameHas(e.name, "I-277") && e.PointAt(e.length * 0.5f).y > up.y, fill, spots, 0f, out sp, out sc);
            Add("i277_brookshire_fill", "I-277 north of uptown (the Brookshire Freeway), its deepest fill", ok, sp, sc);
            // Providence Road where a creek passes under it: its lowest point
            // near the creek it crosses, the fill side
            ok = false; sp = default; sc = 0f;
            {
                float bestY = float.MaxValue;
                if (map.waters != null)
                    foreach (var e in map.edges)
                    {
                        if (e.link || !string.Equals(e.name, "Providence Road", System.StringComparison.OrdinalIgnoreCase)) continue;
                        for (float s = 30f; s <= e.length - 30f; s += 5f)
                        {
                            if (e.ElevatedAt(s)) continue;
                            var p = e.PointAt(s);
                            bool creek = false;
                            waterNear.Clear();
                            map.WaterSegsInRect(p - Vector2.one * 60f, p + Vector2.one * 60f, waterNear);
                            foreach (int packed in waterNear)
                            {
                                var w = map.waters[packed >> 12]; int j = packed & 0xFFF;
                                if (w.lake || w.pts == null || j + 1 >= w.pts.Length || !NameHas(w.name, "Creek")) continue;
                                Vector2 a = w.pts[j], d = w.pts[j + 1] - a;
                                float L2 = d.sqrMagnitude;
                                float tt = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                                if (Vector2.Distance(p, a + d * tt) < 60f) { creek = true; break; }
                            }
                            if (!creek) continue;
                            float y = e.YAt(s);
                            if (y >= bestY) continue;
                            int side = fill(e, s, 1) >= fill(e, s, -1) ? 1 : -1;
                            if (!Clear(map, e, s, side)) side = -side;
                            if (!Clear(map, e, s, side)) continue;
                            bestY = y; sp = new Spot { e = e.index, s = s, side = side }; ok = true; sc = fill(e, s, side);
                        }
                    }
            }
            Add("providence_creek", "Providence Road at the creek it crosses, its lowest point there", ok, sp, sc);
            // three streets across a hillside within 8 km of uptown: the land
            // rising on one side and falling on the other, the road between
            Score hill = (e, s, side) =>
            {
                float hi = DemOff(e, s, side, 15f), lo = DemOff(e, s, -side, 15f);
                if (hi < 2f || lo > -2f) return float.MinValue;
                return Mathf.Min(hi, -lo);
            };
            for (int k = 1; k <= 3; k++)
            {
                ok = Best(map, e => e.cls <= 3 && !string.IsNullOrEmpty(e.name) && !NameHas(e.name, "I-") &&
                                   Vector2.Distance(e.PointAt(e.length * 0.5f), up) < 8000f, hill, spots, 2500f, out sp, out sc);
                Add("hill_" + k, "a street across a hillside: land up on this side, down on the other", ok, sp, sc);
            }
            return spots;
        }

        [MenuItem("PSX Racing/Preview Charlotte Roadside Shots")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRoadsideShots] no city data"); return; }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = System.Environment.GetEnvironmentVariable("PSX_RS_OUT");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(root, "Screenshots", "City", "roadside");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            string spotsPath = System.Environment.GetEnvironmentVariable("PSX_RS_SPOTS");
            if (string.IsNullOrEmpty(spotsPath)) spotsPath = Path.Combine(root, "roadside_spots.txt");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var log = new StringBuilder();

            List<Spot> spots;
            if (File.Exists(spotsPath))
            {
                spots = new List<Spot>();
                foreach (var line in File.ReadAllLines(spotsPath))
                {
                    var f = line.Split('\t');
                    if (f.Length < 5 || line.StartsWith("#")) continue;
                    spots.Add(new Spot { id = f[0], e = int.Parse(f[1]), s = float.Parse(f[2], inv), side = int.Parse(f[3]), what = f[4] });
                }
                log.AppendLine($"spots read from {spotsPath}: {spots.Count}");
            }
            else
            {
                spots = Find(map, log);
                var sf = new StringBuilder("# id\tedge\ts\tside\twhat (CityRoadsideShots)\n");
                foreach (var sp in spots) sf.Append(string.Format(inv, "{0}\t{1}\t{2:0.0}\t{3}\t{4}\n", sp.id, sp.e, sp.s, sp.side, sp.what));
                File.WriteAllText(spotsPath, sf.ToString());
                log.AppendLine($"spots found and written to {spotsPath}");
            }

            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var go = new GameObject("~cityRoadsideShots");
            var world = go.AddComponent<CityWorld>();
            world.materials = PSXRacingBuilder.CityMaterials();
            bool treesWere = CityTrees.Enabled;
            int shots = 0;
            try
            {
                foreach (var sp in spots)
                {
                    if (sp.e < 0 || sp.e >= map.edges.Length) continue;
                    var e = map.edges[sp.e];
                    float s = sp.s;
                    var c = e.PointAt(s); var n = Normal(e, s, sp.side);
                    float hw = EdgeM(e, s, sp.side), y = e.YAt(s);
                    // SEAT: 35 m back in the lane on the graded side, down the road
                    float sb = Mathf.Max(0f, s - 35f), sa = Mathf.Min(e.length, s + 10f);
                    var nb = Normal(e, sb, sp.side); var na = Normal(e, sa, sp.side);
                    var eyeS2 = e.PointAt(sb) + nb * (EdgeM(e, sb, sp.side) * 0.45f);
                    var aimS2 = e.PointAt(sa) + na * (EdgeM(e, sa, sp.side) * 0.45f + 4f);
                    var seatEye = new Vector3(eyeS2.x, e.YAt(sb) + EyeM, eyeS2.y);
                    var seatAim = new Vector3(aimS2.x, e.YAt(sa) + 0.8f, aimS2.y);
                    // HIGH: out over the graded side, 40 m back, above the land
                    // there, looking back at the road's edge: the section's
                    // bank seen from outside (up a fill, down into a cut)
                    var t = e.TangentAt(s);
                    var hiEye2 = c - t * 40f + n * (hw + 30f);
                    var hiAim2 = c + t * 10f + n * (hw * 0.5f);
                    float eyeY = Mathf.Max(y + 7f, CityElevation.BaseY(hiEye2.x, hiEye2.y) + 6f);
                    var hiEye = new Vector3(hiEye2.x, eyeY, hiEye2.y);
                    var hiAim = new Vector3(hiAim2.x, y - 1f, hiAim2.y);

                    CityTrees.Enabled = true;
                    world.DropAll();
                    world.EnsureRing(seatEye, 1); world.EnsureRing(new Vector3(c.x, y, c.y), 1); world.EnsureRing(hiEye, 1);
                    Shoot(dir, sp.id + "_seat", seatEye, Quaternion.LookRotation(seatAim - seatEye), FovDeg, FarM);
                    Shoot(dir, sp.id + "_high", hiEye, Quaternion.LookRotation(hiAim - hiEye), FovDeg, FarM);
                    CityTrees.Enabled = false;
                    world.DropAll();
                    world.EnsureRing(new Vector3(c.x, y, c.y), 1); world.EnsureRing(hiEye, 1);
                    Shoot(dir, sp.id + "_bare", hiEye, Quaternion.LookRotation(hiAim - hiEye), FovDeg, FarM);
                    shots += 3;

                    // the section at the spot: road, DEM, graded ground, both sides
                    log.Append(string.Format(inv, "{0} e{1} '{2}' s={3:0} graded side {4} ({5:0},{6:0}) road y {7:0.00}: {8}\n", sp.id, sp.e, e.name, s, sp.side < 0 ? "L" : "R", c.x, c.y, y, sp.what));
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        int side = sp.side * sd;
                        float h = EdgeM(e, s, side); var nn = Normal(e, s, side);
                        log.Append(sd > 0 ? "   graded side " : "   other side  ");
                        foreach (float o in Out)
                        {
                            var q = c + nn * (h + o);
                            log.Append(string.Format(inv, " {0,2:0} m: dem {1,6:+0.0;-0.0} ground {2,6:+0.0;-0.0} |", o, CityElevation.BaseY(q.x, q.y) - y, CityElevation.GroundY(map, q.x, q.y) - y));
                        }
                        log.Append('\n');
                    }
                    Debug.Log($"[CityRoadsideShots] {sp.id}: e{sp.e} '{e.name}' s={s:0} side {sp.side}");
                }
            }
            finally
            {
                CityTrees.Enabled = treesWere;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "roadside_shots.txt"), log.ToString());
            Debug.Log($"[CityRoadsideShots] {shots} shots to {dir}\n{log}");
        }

        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float fov, float far)
        {
            var camGO = new GameObject("~roadsideCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = far;
            cam.fieldOfView = fov;
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
