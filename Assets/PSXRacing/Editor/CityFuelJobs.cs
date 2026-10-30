using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CITY FUEL r1's sandbox proof, lean (the owner's usage): as few Unity
    /// jobs as the checks allow.
    ///   BuildAndShoot  the town station as the last build left it (BEFORE,
    ///                  once), the full scene build (BakeCityStations inside),
    ///                  the town AFTER, one shot per circuit fuel stop with its
    ///                  clearance check (the station's solids off the track and
    ///                  its verge, the forecourt off the lanes), Charlotte from a
    ///                  car at W 10th St, Carowinds Blvd, Westinghouse Blvd near
    ///                  I-77 and a truck stop by day (and two of them at night),
    ///                  the stations' install line, and the drive audit
    ///                  (PSX_DRIVE_ROUTES picks its routes).
    ///   FrameCost      Carowinds Blvd and E Independence Blvd, the ring built
    ///                  whole with the stations and without (a map parsed with
    ///                  PSX_CITY_FUEL's switch off): tile build p50/p95/max and
    ///                  the draws in view, LOD levels as a camera there picks them.
    /// Screenshots/CityFuel/*.png, PSXRacing_city_fuel_jobs.txt (appended per job).
    /// </summary>
    public static class CityFuelJobs
    {
        const int W = 1280, H = 720;
        const float Fov = 58f, Far = 500f;
        static StringBuilder rep;
        static Color? nightSky;
        static string Root => Directory.GetParent(Application.dataPath).FullName;
        static string Dir => Path.Combine(Root, "Screenshots", "CityFuel");
        static string RepPath => Path.Combine(Root, "PSXRacing_city_fuel_jobs.txt");

        static void R(string s) { rep.AppendLine(s); Debug.Log("[CityFuel] " + s); }
        static void Save() => File.AppendAllText(RepPath, rep.ToString());

        static void Phase(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { R("FAILED " + name + ": " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }

        // ==================================================================
        //  JOB 1
        // ==================================================================
        public static void BuildAndShoot()
        {
            rep = new StringBuilder("==== BuildAndShoot " + DateTime.Now.ToString("s") + "\n");
            Directory.CreateDirectory(Dir);
            const string town = "Assets/PSXRacing/Scenes/Town.unity";
            try
            {
                if (File.Exists(Path.Combine(Root, town)) && !File.Exists(Path.Combine(Dir, "town_before_oblique.png")))
                    Phase("town before", () => TownShots(town, "before"));
                PSXRacingBuilder.Build();
                string bl = Path.Combine(Root, "PSXRacing_build_log.txt");
                bool ok = File.Exists(bl) && File.ReadAllText(bl).Contains("BUILD OK");
                R("scene build: " + (ok ? "BUILD OK" : "NOT OK (see PSXRacing_build_log.txt)"));
                if (!ok) return;
                Phase("stations bake", () =>
                {
                    foreach (var l in File.ReadAllLines(Path.Combine(Root, "PSXRacing_city_stations.txt"))) R("  bake: " + l);
                });
                Phase("town after", () => TownShots(town, "after"));
                Phase("circuits", Circuits);
                Phase("charlotte", Charlotte);
                Phase("drive audit", DriveAudit);
            }
            finally { Save(); }
        }

        /// <summary>The stations' parts baked again (BakeCityStations: the
        /// solids fitted to what is drawn) and the drive audit on them, in one
        /// Unity run (PSX_DRIVE_ROUTES picks its routes).</summary>
        public static void BakeAndDrive()
        {
            rep = new StringBuilder("==== BakeAndDrive " + DateTime.Now.ToString("s") + "\n");
            try
            {
                bool ok = true;
                Phase("stations bake", () =>
                {
                    foreach (var l in PSXRacingBuilder.BakeCityStations()) { R("  bake: " + l); if (l.StartsWith("FAILED")) ok = false; }
                });
                if (ok) Phase("drive audit", DriveAudit);
                else R("stations bake FAILED: no drive audit");
            }
            finally { Save(); }
        }

        /// <summary>FrameCost, then the city refuel check
        /// (CityFuelPlayCheck.Run: it enters play mode and exits the editor
        /// itself; run it with no -quit) - two jobs in one Unity run.</summary>
        public static void FrameCostThenCityCheck()
        {
            FrameCost();
            CityFuelPlayCheck.Run();
        }

        static void DriveAudit()
        {
            CityAudit.RunDrive();
            var f = Path.Combine(Root, "city_drive.txt");
            if (!File.Exists(f)) { R("drive audit: no city_drive.txt"); return; }
            foreach (var l in File.ReadAllLines(f))
                if (l.Contains("FAIL") || l.Contains("ok   ") || l.StartsWith("drive audit") || l.Contains("SOLIDS:")) R("  drive: " + l.Trim());
        }

        // ---- the town ----------------------------------------------------
        static void TownShots(string path, string tag)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var gs = GameObject.Find("GasStation");
            int pumps = UnityEngine.Object.FindObjectsByType<GasPump>(FindObjectsInactive.Include).Length;
            var b = gs != null ? BoundsOf(gs) : new Bounds();
            R($"town {tag}: station {(gs != null ? "found" : "NOT FOUND")}, {pumps} pump volumes, bounds {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m at ({b.center.x:0.0}, {b.center.z:0.0}); " +
              $"canopies {CountNamed(gs, "The_ceiling")}, shops {CountNamed(gs, "6twelve")}, pump meshes {CountPrefix(gs, "Fuel_pump")}");
            DayLight();
            // fixed cameras about BuildTownStation's spot, so before and after match
            var at = new Vector3(-52f, 2f, 26f);
            Shoot("town_" + tag + "_oblique", at + new Vector3(32f, 16f, -40f), at);
            Shoot("town_" + tag + "_overview", at + new Vector3(0f, 58f, -30f), at + new Vector3(0f, 0f, 4f));
        }

        static int CountNamed(GameObject root, string n)
        {
            if (root == null) return 0;
            int c = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) c++;
            return c;
        }

        static int CountPrefix(GameObject root, string p)
        {
            if (root == null) return 0;
            int c = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) if (r.name.StartsWith(p, StringComparison.Ordinal)) c++;
            return c;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ---- the circuits ------------------------------------------------
        static readonly string[] NotCircuits = { "Charlotte", "TryonSprint", "UptownLoop", "IndependenceSprint", "Town", "Garage", "Pizzeria", "SellerLot", "Neighborhood", "LifeHome" };

        static void Circuits()
        {
            foreach (var f in Directory.GetFiles(Path.Combine(Application.dataPath, "PSXRacing", "Scenes"), "*.unity"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (Array.IndexOf(NotCircuits, name) >= 0) continue;
                EditorSceneManager.OpenScene("Assets/PSXRacing/Scenes/" + name + ".unity", OpenSceneMode.Single);
                var gs = GameObject.Find("GasStation");
                if (gs == null) continue;
                var path = UnityEngine.Object.FindAnyObjectByType<TrackPath>();
                if (path == null || path.waypoints == null || path.waypoints.Length < 4) { R($"circuit {name}: station but no TrackPath"); continue; }
                var wp = path.waypoints;
                float half = path.roadWidth * 0.5f;
                bool loop = !path.pointToPoint;
                float Dist(Vector3 p)
                {
                    float best = float.MaxValue;
                    int n = wp.Length, segs = loop ? n : n - 1;
                    for (int i = 0; i < segs; i++)
                    {
                        Vector2 a = new Vector2(wp[i].x, wp[i].z), c = new Vector2(wp[(i + 1) % n].x, wp[(i + 1) % n].z), q = new Vector2(p.x, p.z);
                        var ab = c - a; float l2 = ab.sqrMagnitude;
                        float t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / l2) : 0f;
                        best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
                    }
                    return best;
                }
                // the station's solids: their boxes' corners
                float minSolid = float.MaxValue; string near = "";
                foreach (var col in gs.GetComponentsInChildren<Collider>(true))
                {
                    if (col.isTrigger || col.gameObject.layer != 9) continue;
                    var cb = col.bounds;
                    for (int k = 0; k < 5; k++)
                    {
                        var p = k == 4 ? cb.center : new Vector3((k & 1) == 0 ? cb.min.x : cb.max.x, 0f, (k & 2) == 0 ? cb.min.z : cb.max.z);
                        float d = Dist(p);
                        if (d < minSolid) { minSolid = d; near = col.name; }
                    }
                }
                // the forecourt: every vertex
                float minFore = float.MaxValue;
                var fore = GameObject.Find("Forecourt");
                var mf = fore != null ? fore.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh != null)
                    foreach (var v in mf.sharedMesh.vertices) minFore = Mathf.Min(minFore, Dist(fore.transform.TransformPoint(v)));
                bool ok = minSolid >= half + 2f && (minFore == float.MaxValue || minFore >= half + 0.3f);
                var sb = BoundsOf(gs);
                R($"circuit {name}: station {sb.size.x:0.0} x {sb.size.z:0.0} m, {gs.GetComponentsInChildren<GasPump>(true).Length} pump volumes " +
                  $"{UnityEngine.Object.FindObjectsByType<GasPump>(FindObjectsInactive.Include).Length} in scene; nearest solid {minSolid:0.0} m from the centreline ({near}), " +
                  $"forecourt {(minFore == float.MaxValue ? "none" : minFore.ToString("0.0") + " m")}, road half {half:0.0} m -> {(ok ? "ok" : "FAIL")} " +
                  "(solids >= road half + 2 m: off the road and its verge; forecourt >= road half + 0.3 m: off the lanes)");
                // the shot: from the track 55 m before the stop, 3 m up
                int pi = 0; float pd = float.MaxValue;
                for (int i = 0; i < wp.Length; i++) { float d = (new Vector2(wp[i].x - sb.center.x, wp[i].z - sb.center.z)).sqrMagnitude; if (d < pd) { pd = d; pi = i; } }
                int back = pi; float run = 0f;
                while (run < 55f) { int prev = loop ? (back - 1 + wp.Length) % wp.Length : Mathf.Max(0, back - 1); if (prev == back) break; run += Vector3.Distance(wp[prev], wp[back]); back = prev; }
                DayLight();
                Shoot("circuit_" + name, wp[back] + Vector3.up * 3f, new Vector3(sb.center.x, wp[pi].y + 2.5f, sb.center.z));
            }
        }

        // ---- Charlotte ---------------------------------------------------
        static void Charlotte()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PSXRacingBuilder.EnsureCityTextures();
            var map = CityMap.Get();
            if (map == null) { R("charlotte: no city data"); return; }
            int refound = 0, cuts = 0;
            foreach (var s in map.stations) { refound += s.cutsRefound; cuts += s.cuts; }
            R($"charlotte install: {CityStations.LastReport}; stale {map.stationsStale}; curb cuts {cuts} ({refound} re-found)");

            var picks = new List<(string name, CityStations.Station st)>();
            void Pick(string tag, Func<CityStations.Station, bool> want, Func<CityStations.Station, float> score)
            {
                CityStations.Station best = null; float bs = float.MaxValue;
                foreach (var s in map.stations) if (want(s)) { float v = score(s); if (v < bs) { bs = v; best = s; } }
                if (best != null) picks.Add((tag, best)); else R($"charlotte: no station for {tag}");
            }
            string Front(CityStations.Station s) => s.frontEdge >= 0 && s.frontEdge < map.edges.Length ? (map.edges[s.frontEdge].name ?? "") : "";
            bool Has(string n, string p) => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0;
            Pick("w10th", s => Has(Front(s), "10th"), s => (s.centre - map.uptown).sqrMagnitude);
            Pick("carowinds", s => Has(Front(s), "Carowinds"), s => -Crowd(map, s));
            Pick("westinghouse", s => Has(Front(s), "Westinghouse"), s => ToMotorway(map, s));
            Pick("truckstop", s => s.cls == CityStations.ClsTruck, s => s.layout == CityStations.LayTruck ? 0f : 1f);

            var go = new GameObject("~cityFuelShots");
            var world = go.AddComponent<CityWorld>();
            GameObject sunGo = null;
            try
            {
                DayLight();
                foreach (var (tag, st) in picks)
                {
                    world.EnsureRing(new Vector3(st.centre.x, 0f, st.centre.y), 1);
                    CarView(map, st, out var eye, out var look);
                    world.EnsureRing(eye, 1);
                    Shoot("city_" + tag + "_day", eye, look);
                    var e = st.frontEdge >= 0 && st.frontEdge < map.edges.Length ? map.edges[st.frontEdge] : null;
                    R($"charlotte {tag}: station {st.index} {st.OsmName} on '{Front(st)}' layout {st.layout} x{st.modules}{(st.Mirror ? " mirrored" : "")} flags {st.flags}, " +
                      $"{st.drives.Length} driveways ({st.cuts} cuts, {st.cutsRefound} re-found), {st.replaced.Length} boxes replaced, lot ({st.centre.x:0}, {st.centre.y:0}); " +
                      $"stood {(world.GetComponentsInChildren<Transform>(true).Length > 0 ? CountStood(world, st) : 0)} part(s)");
                    world.DropAll();
                }
                // night at two of them: the hour through TimeOfDay, lamps lit by hand (edit mode runs no Awake)
                sunGo = new GameObject("~fuelSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                var globals = sunGo.AddComponent<PSXGlobals>();
                globals.sun = sun;
                TimeOfDay.Apply(TimeOfDay.Night, sun);
                globals.Apply();
                nightSky = globals.fogColor;
                int nights = 0;
                foreach (var (tag, st) in picks)
                {
                    if (tag != "w10th" && tag != "carowinds") continue;
                    world.EnsureRing(new Vector3(st.centre.x, 0f, st.centre.y), 1);
                    CarView(map, st, out var eye, out var look);
                    world.EnsureRing(eye, 1);
                    NightGlow.PreviewAll(true);
                    globals.Apply();
                    Shoot("city_" + tag + "_night", eye, look);
                    nights++;
                    world.DropAll();
                }
                R($"charlotte: {picks.Count} day shots, {nights} night shots");
            }
            finally
            {
                nightSky = null;
                if (world != null) world.DropAll();
                UnityEngine.Object.DestroyImmediate(go);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
            }
        }

        static int CountStood(CityWorld world, CityStations.Station st)
        {
            int n = 0;
            foreach (var t in world.GetComponentsInChildren<Transform>(true))
                if (t.name == "FuelStation_" + st.index) n += t.childCount;
            return n;
        }

        /// <summary>How many stations stand within 400 m (Carowinds' cluster).</summary>
        static float Crowd(CityMap map, CityStations.Station s)
        {
            int n = 0;
            foreach (var o in map.stations) if ((o.centre - s.centre).sqrMagnitude < 400f * 400f) n++;
            return n;
        }

        /// <summary>How far to the nearest motorway (I-77 at Westinghouse).</summary>
        static float ToMotorway(CityMap map, CityStations.Station s)
        {
            float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.cls < 5 || e.link) continue;
                for (int i = 0; i < e.pts.Length; i += 4) best = Mathf.Min(best, (e.pts[i] - s.centre).sqrMagnitude);
            }
            return best;
        }

        /// <summary>A driver's eye on the station's frontage, 45 m up the
        /// road in the lane on its side, looking at the canopy.</summary>
        static void CarView(CityMap map, CityStations.Station st, out Vector3 eye, out Vector3 look)
        {
            int ei = st.frontEdge; float s = st.frontS;
            if (ei < 0 || ei >= map.edges.Length || map.stationsStale)
                map.NearestRoadPoint(st.origin, 200f, false, out ei, out s, out _);
            var e = map.edges[ei];
            s = Mathf.Clamp(s, 0f, e.length);
            float sb = s - 45f >= 0f ? s - 45f : Mathf.Min(e.length, s + 45f);
            var t = e.TangentAt(sb);
            var left = new Vector2(-t.y, t.x);
            var toStation = st.centre - e.PointAt(s);
            float side = Vector2.Dot(toStation, left) >= 0f ? 1f : -1f;
            var p = e.PointAt(sb) + left * side * (e.HalfMax * 0.5f);
            eye = new Vector3(p.x, e.YAt(sb) + 1.3f, p.y);
            var c = st.World(0f, CityStations.CanV0 + 0.5f * CityStations.CanD);
            look = new Vector3(c.x, e.YAt(s) + 2.5f, c.y);
        }

        // ==================================================================
        //  JOB 6: the frame cost, stations on and off
        // ==================================================================
        public static void FrameCost()
        {
            rep = new StringBuilder("==== FrameCost " + DateTime.Now.ToString("s") + "\n");
            try
            {
                PSXRacingBuilder.EnsureCityTextures();
                var map = CityMap.Get();
                if (map == null || map.stations.Length == 0) { R("frame cost: no city or no stations"); return; }
                var sites = new List<(string name, Vector2 at)>();
                // Carowinds Blvd: the most stations within 400 m on it
                CityStations.Station car = null; float cn = -1f;
                foreach (var s in map.stations)
                {
                    var fe = s.frontEdge >= 0 && s.frontEdge < map.edges.Length ? map.edges[s.frontEdge].name ?? "" : "";
                    if (fe.IndexOf("Carowinds", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    float n = Crowd(map, s);
                    if (n > cn) { cn = n; car = s; }
                }
                if (car != null) sites.Add(("carowinds", car.origin));
                // E Independence Blvd: the 256 m tile holding the most of its stations
                var byTile = new Dictionary<long, List<CityStations.Station>>();
                foreach (var s in map.stations)
                {
                    var fe = s.frontEdge >= 0 && s.frontEdge < map.edges.Length ? map.edges[s.frontEdge].name ?? "" : "";
                    if (fe.IndexOf("Independence", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    long k = ((long)Mathf.FloorToInt(s.centre.x / 256f) << 32) ^ (uint)Mathf.FloorToInt(s.centre.y / 256f);
                    if (!byTile.TryGetValue(k, out var l)) byTile[k] = l = new List<CityStations.Station>();
                    l.Add(s);
                }
                List<CityStations.Station> most = null;
                foreach (var l in byTile.Values) if (most == null || l.Count > most.Count) most = l;
                if (most != null)
                {
                    var m = Vector2.zero; foreach (var s in most) m += s.origin; m /= most.Count;
                    sites.Add(("e_independence", m));
                }
                R($"frame cost sites: {string.Join(", ", sites.ConvertAll(x => $"{x.name} ({x.at.x:0}, {x.at.y:0})"))}; LOD bias {QualitySettings.lodBias}");
                Measure(map, sites, "stations ON");
                // the same city parsed without them
                var cityTa = Resources.Load<TextAsset>("charlotte_city");
                var bldTa = Resources.Load<TextAsset>("charlotte_bld");
                CityStations.On = false;
                CityMap off;
                try { off = CityMap.Parse(cityTa.bytes, bldTa != null ? bldTa.bytes : null); }
                finally { CityStations.On = true; }
                var loaded = typeof(CityMap).GetField("loaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (loaded == null) { R("frame cost: CityMap.loaded not found - no OFF pass"); return; }
                loaded.SetValue(null, off);
                try { Measure(off, sites, "stations OFF"); }
                finally { loaded.SetValue(null, map); }
            }
            finally { Save(); }
        }

        static void Measure(CityMap map, List<(string name, Vector2 at)> sites, string tag)
        {
            var go = new GameObject("~fuelCost");
            var world = go.AddComponent<CityWorld>();
            world.ring = 2;
            var timings = new List<CityWorld.TileTiming>();
            Action<CityWorld.TileTiming> on = t => timings.Add(t);
            CityWorld.TileBuilt += on;
            var camGo = new GameObject("~fuelCostEye");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = Fov; cam.aspect = 16f / 9f; cam.nearClipPlane = 0.3f; cam.farClipPlane = Far;
            try
            {
                // a warm-up tile, not counted
                world.EnsureTile(Mathf.FloorToInt(map.uptown.x / 256f), Mathf.FloorToInt(map.uptown.y / 256f));
                world.DropAll();
                foreach (var (name, at) in sites)
                {
                    timings.Clear();
                    int stood0 = CityWorld.StationsStood;
                    world.EnsureRing(new Vector3(at.x, 0f, at.y), 2);
                    var tot = new List<float>(); float standMax = 0f;
                    foreach (var t in timings) if (!t.treeFrame) { tot.Add(t.totalMs); standMax = Mathf.Max(standMax, t.standUpMs); }
                    tot.Sort();
                    float P(float q) => tot.Count == 0 ? 0f : tot[Mathf.Clamp(Mathf.CeilToInt(q / 100f * tot.Count) - 1, 0, tot.Count - 1)];
                    int draws = 0, dmax = 0;
                    if (map.NearestRoadPoint(at, 200f, false, out int ei, out float s, out _))
                    {
                        var e = map.edges[ei]; var p = e.PointAt(s); var fwd = e.TangentAt(s);
                        var eye = new Vector3(p.x, e.YAt(s) + 1.2f, p.y);
                        for (int h = 0; h < 4; h++)
                        {
                            var d = Quaternion.Euler(0f, 90f * h, 0f) * new Vector3(fwd.x, 0f, fwd.y);
                            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d, Vector3.up));
                            int n = DrawsInView(cam, go);
                            draws += n; dmax = Mathf.Max(dmax, n);
                        }
                    }
                    int stationDraws = 0;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("FuelStation_", StringComparison.Ordinal))
                            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) stationDraws += Mathf.Max(1, r.sharedMaterials.Length);
                    R($"  {tag,-12} {name,-14} {tot.Count} tiles: tile build p50 {P(50):0.0} p95 {P(95):0.0} max {P(100):0.0} ms (stand-up max {standMax:0.0} ms); " +
                      $"stations stood {CityWorld.StationsStood - stood0}; draws in view max {dmax} (4 headings, LOD as seen; {stationDraws} station renderer slots all LODs)");
                    world.DropAll();
                }
            }
            finally
            {
                CityWorld.TileBuilt -= on;
                world.DropAll();
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>Draws a camera would issue here: every enabled renderer in
        /// its frustum, a LODGroup's only at the level this distance picks.</summary>
        static int DrawsInView(Camera cam, GameObject root)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var vis = new Dictionary<Renderer, bool>();
            float k = 2f * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
            foreach (var g in root.GetComponentsInChildren<LODGroup>(false))
            {
                var lods = g.GetLODs();
                float d = Mathf.Max(0.01f, Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint)));
                float h = g.size * Mathf.Abs(g.transform.lossyScale.x) / (d * k) * QualitySettings.lodBias;
                int active = -1;
                for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) { active = i; break; }
                for (int i = 0; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null) vis[r] = (vis.TryGetValue(r, out bool v) && v) || i == active;
            }
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (vis.TryGetValue(r, out bool on) && !on) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                n += Mathf.Max(1, r.sharedMaterials.Length);
            }
            return n;
        }

        // ---- light and camera --------------------------------------------
        static void DayLight()
        {
            nightSky = null;
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", Far);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            Shader.SetGlobalFloat("_PSXNight", 0f);
        }

        static void Shoot(string name, Vector3 eye, Vector3 look)
        {
            var camGO = new GameObject("~fuelCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = nightSky ?? new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = Far;
            cam.fieldOfView = Fov;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGO);
        }
    }
}
