using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BUILDING PASS, photographed and counted (2026-10-28, the owner's
    /// "the city building textures ... look extremely unprofessional"): the
    /// four reference places at the chase camera's height, noon and night,
    /// through CityPreview.RunUptownRef (the game's own hours, tiles, far
    /// buildings and skyline), plus the per-tile cost of three tiles (two
    /// uptown, one South End) built through CityMeshes.Build - triangles and
    /// submeshes (one draw each) of every mesh a tile makes.
    ///   tryon_start   the Tryon Sprint's South End start (route "tryon", 60 m in), up the route
    ///   tryon_25s     the same route ~650 m in (the owner's first 25 s): the towers ahead
    ///   trade_tryon   N Tryon toward the Square (Trade &amp; Tryon)
    ///   noda          N Davidson St at E 36th St, down the low-rise strip
    ///   i277_skyline  I-277 south-east of uptown, looking at the skyline
    /// PSX_BP_TAG names the run (before / after). PNGs:
    /// Screenshots\bp_&lt;tag&gt;_&lt;view&gt;_&lt;hour&gt;.png; counts: Screenshots\bp_&lt;tag&gt;_tiles.txt.
    /// Headless (graphics on, -force-glcore):
    /// -executeMethod PSXRacing.EditorTools.BuildingPassShots.Run
    /// </summary>
    public static class BuildingPassShots
    {
        const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;   // CityRefSpots.LL

        static string LatLon(Vector2 p)
        {
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            double lat = Lat0 + p.y / (111132.0 * CityMap.LayoutScale);
            double lon = Lon0 + p.x / (mLon * CityMap.LayoutScale);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return lat.ToString("F6", ci) + "," + lon.ToString("F6", ci);
        }

        /// <summary>One RunUptownRef extra view: the eye at <paramref name="eye"/>
        /// (eyeAgl over the ground), looking at <paramref name="aim"/> (aimAgl).</summary>
        static string View(string name, Vector2 eye, Vector2 aim, float eyeAgl, float aimAgl, float fov)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Vector2 d = eye - aim;
            float dist = d.magnitude;
            float brg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;   // compass bearing of the eye from the aim
            return string.Join(",", name, LatLon(aim), aimAgl.ToString("F1", ci), dist.ToString("F1", ci),
                               brg.ToString("F1", ci), eyeAgl.ToString("F1", ci), fov.ToString("F0", ci));
        }

        /// <summary>The tryon route's plan polyline (charlotte_routes.json).</summary>
        static List<Vector2> TryonRoute()
        {
            var pts = new List<Vector2>();
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "PSXRacing/Resources/charlotte_routes.json"));
            int at = json.IndexOf("\"id\":\"tryon\"");
            if (at < 0) return pts;
            int p0 = json.IndexOf("\"pts\":[", at);
            if (p0 < 0) return pts;
            p0 += 7;
            int p1 = json.IndexOf("]", p0);
            var f = json.Substring(p0, p1 - p0).Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i + 1 < f.Length; i += 2)
                pts.Add(new Vector2(float.Parse(f[i], ci), float.Parse(f[i + 1], ci)));
            return pts;
        }

        static Vector2 Along(List<Vector2> pl, float s, out Vector2 dir)
        {
            dir = Vector2.up;
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                float l = Vector2.Distance(pl[i], pl[i + 1]);
                if (l < 1e-3f) continue;
                dir = (pl[i + 1] - pl[i]) / l;
                if (s <= l) return pl[i] + dir * s;
                s -= l;
            }
            return pl[pl.Count - 1];
        }

        /// <summary>The junction of two named streets: the closest pair of
        /// their edges' end points.</summary>
        static bool Junction(CityMap map, string a, string b, out Vector2 j, out Vector2 alongA)
        {
            j = default; alongA = Vector2.up;
            float best = 900f;
            foreach (var ea in map.edges)
            {
                if (ea.name == null || ea.pts == null || ea.pts.Length < 2 || !ea.name.Contains(a)) continue;
                foreach (var eb in map.edges)
                {
                    if (eb.name == null || eb.pts == null || eb.pts.Length < 2 || !eb.name.Contains(b)) continue;
                    foreach (int ia in new[] { 0, ea.pts.Length - 1 })
                        foreach (int ib in new[] { 0, eb.pts.Length - 1 })
                        {
                            float d2 = (ea.pts[ia] - eb.pts[ib]).sqrMagnitude;
                            if (d2 >= best) continue;
                            best = d2; j = ea.pts[ia];
                            var other = ea.pts[ia == 0 ? 1 : ea.pts.Length - 2];
                            alongA = (other - j).normalized;
                        }
                }
            }
            return best < 900f;
        }

        /// <summary>The after run: PSX/Lit compiled for the player's API
        /// (FacadeStructureShots.CompileCheck), the Enhanced map rebuilt (the
        /// twin is 4096 wide now: its importer's cap follows the manifest),
        /// then <see cref="Run"/>.
        /// Headless: -executeMethod PSXRacing.EditorTools.BuildingPassShots.RunAfter</summary>
        public static void RunAfter()
        {
            FacadeStructureShots.CompileCheck();
            try { TextureTierTool.BuildMap(); }
            catch (System.Exception ex) { Debug.LogError("[BuildingPass] BuildMap: " + ex); }
            Run();
        }

        /// <summary>One Unity job for both halves of a run: the shots and
        /// counts, then PerfProbe.Run (its own scene, play and exit; set
        /// PSX_PERF_VENUE=TryonSprint and PSX_PERF_TAG). With after = true the
        /// <see cref="RunAfter"/> steps first.</summary>
        public static void RunThenPerf() { Run(); PerfProbe.Run(); }
        public static void RunAfterThenPerf() { RunAfter(); PerfProbe.Run(); }

        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[BuildingPass] no city data"); return; }
            string tag = System.Environment.GetEnvironmentVariable("PSX_BP_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "run";
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);

            var views = new List<string>();
            var names = new List<string>();
            var route = TryonRoute();
            Vector2 sStart = default, sMid = default;
            if (route.Count > 2)
            {
                sStart = Along(route, 60f, out var d0);
                views.Add(View("tryon_start", sStart, sStart + d0 * 320f, 2.6f, 22f, 65f)); names.Add("tryon_start");
                sMid = Along(route, 650f, out var d1);
                views.Add(View("tryon_25s", sMid, sMid + d1 * 320f, 2.6f, 30f, 65f)); names.Add("tryon_25s");
            }
            else Debug.LogWarning("[BuildingPass] no tryon route");
            // the Square, down N Tryon (FacadeStructureShots' tryon_street, at chase height)
            var sq = CityRefSpots.LL(35.22732, -80.84286);
            var sqEye = sq + new Vector2(Mathf.Sin(49f * Mathf.Deg2Rad), Mathf.Cos(49f * Mathf.Deg2Rad)) * 250f;
            views.Add(View("trade_tryon", sqEye, sq, 2.6f, 45f, 65f)); names.Add("trade_tryon");
            if (Junction(map, "Davidson", "36th", out var nj, out var nAlong))
            {
                // down the strip from just inside the junction
                views.Add(View("noda", nj + nAlong * 12f, nj + nAlong * 170f, 2.6f, 5f, 65f)); names.Add("noda");
            }
            else Debug.LogWarning("[BuildingPass] no N Davidson / 36th junction");
            // I-277: the freeway point 800-1200 m from the core, nearest bearing 150 (south-south-east)
            Vector2 core = map.uptown, eye277 = default; float bestScore = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.name == null || e.pts == null || !(e.name.Contains("277") || e.name.Contains("Belk"))) continue;
                foreach (var p in e.pts)
                {
                    float r = Vector2.Distance(p, core);
                    if (r < 800f || r > 1200f) continue;
                    float brg = Mathf.Atan2(p.x - core.x, p.y - core.y) * Mathf.Rad2Deg;
                    float score = Mathf.Abs(Mathf.DeltaAngle(brg, 150f));
                    if (score < bestScore) { bestScore = score; eye277 = p; }
                }
            }
            if (bestScore < 60f) { views.Add(View("i277_skyline", eye277, core, 2.6f, 90f, 40f)); names.Add("i277_skyline"); }
            else Debug.LogWarning("[BuildingPass] no I-277 eye");

            // PSX_BP_VIEWS: a subset by name
            string only = System.Environment.GetEnvironmentVariable("PSX_BP_VIEWS");
            if (!string.IsNullOrEmpty(only))
            {
                var keepNames = new HashSet<string>(only.Split(','));
                for (int k = names.Count - 1; k >= 0; k--)
                    if (!keepNames.Contains(names[k])) { names.RemoveAt(k); views.RemoveAt(k); }
            }
            System.Environment.SetEnvironmentVariable("PSX_UPTOWN_EXTRA", string.Join(";", views));
            System.Environment.SetEnvironmentVariable("PSX_UPTOWN_REF", string.Join(",", names));
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_UPTOWN_HOURS")))
                System.Environment.SetEnvironmentVariable("PSX_UPTOWN_HOURS", "noon,night");
            Debug.Log("[BuildingPass] views: " + string.Join(" | ", views));
            if (System.Environment.GetEnvironmentVariable("PSX_BP_SHOTS") != "0")
            {
                foreach (var f in Directory.GetFiles(dir, "uptownref_*.png")) File.Delete(f);
                CityPreview.RunUptownRef();
                foreach (var f in Directory.GetFiles(dir, "uptownref_*.png"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Substring("uptownref_".Length);
                    string to = Path.Combine(dir, "bp_" + tag + "_" + n + ".png");
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(f, to);
                }
            }
            TileCounts(map, Path.Combine(dir, "bp_" + tag + "_tiles.txt"), sq, sStart);
            Debug.Log("[BuildingPass] done: Screenshots\\bp_" + tag + "_*");
            // PSX_BP_EXIT=1: a job of this alone ends here (a chained PerfProbe exits by itself)
            if (System.Environment.GetEnvironmentVariable("PSX_BP_EXIT") == "1") UnityEditor.EditorApplication.Exit(0);
        }

        /// <summary>Triangles and submeshes (draws) of every mesh three tiles
        /// make: the Square's tile, the tile south-west of it, the Tryon
        /// start's tile in South End.</summary>
        static void TileCounts(CityMap map, string file, Vector2 square, Vector2 southEnd)
        {
            var sb = new StringBuilder("tile\ttx\ttz\tfootprints\tbldTris\tbldDraws\tallTris\tallDraws\tperMesh\n");
            var go = new GameObject("~buildingPassCounts");
            var world = go.AddComponent<CityWorld>();
            try
            {
                world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 0);
                world.DropAll();
                float ts = CityMeshes.TileSize;
                var tiles = new List<(string, int, int)>
                {
                    ("uptown_square", Mathf.FloorToInt(square.x / ts), Mathf.FloorToInt(square.y / ts)),
                    ("uptown_sw", Mathf.FloorToInt(square.x / ts) - 1, Mathf.FloorToInt(square.y / ts) - 1),
                    ("southend_start", Mathf.FloorToInt(southEnd.x / ts), Mathf.FloorToInt(southEnd.y / ts)),
                };
                foreach (var (name, tx, tz) in tiles)
                {
                    var tm = CityMeshes.Build(map, world.NodeTrims, world.Buildings, tx, tz);
                    int allT = 0, allD = 0, bT = 0, bD = 0;
                    var per = new StringBuilder();
                    foreach (var fi in typeof(CityMeshes.TileMeshes).GetFields())
                    {
                        if (fi.FieldType != typeof(Mesh)) continue;
                        var m = fi.GetValue(tm) as Mesh;
                        if (m == null) continue;
                        int t = 0, d = 0;
                        for (int s = 0; s < m.subMeshCount; s++)
                        {
                            int ic = (int)m.GetIndexCount(s);
                            if (ic > 0) { t += ic / 3; d++; }
                        }
                        // a collider mesh is cooked, never drawn
                        if (!fi.Name.ToLowerInvariant().Contains("collider")) { allT += t; allD += d; }
                        if (fi.Name == "buildings") { bT = t; bD = d; }
                        if (t > 0) per.Append(fi.Name).Append('=').Append(t).Append('/').Append(d).Append(' ');
                    }
                    sb.Append($"{name}\t{tx}\t{tz}\t{tm.footprintCount}\t{bT}\t{bD}\t{allT}\t{allD}\t{per}\n");
                }
            }
            catch (System.Exception ex) { sb.Append("FAILED " + ex + "\n"); }
            finally { Object.DestroyImmediate(go); }
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[BuildingPass] tiles:\n" + sb);
        }
    }
}
