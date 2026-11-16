using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WATER, BEFORE AND AFTER (the water round, 2026-11-16). One process
    /// shoots every pose with whatever PSX_WATER_HD says (run it twice, =0 and
    /// =1, in fresh processes); frames go to Screenshots\water\&lt;pose&gt;_&lt;hd|base&gt;.png
    /// at PSX_WATERSHOT_LINES (default 1080) lines 16:9 through PSX/Blit.
    ///   EmeraldIsle   the surf off the beach: noon, sunset, night, rain; and
    ///                 toward the low sun at sunset
    ///   LangstonBridge  Bogue Sound under the bridge: from the deck (noon,
    ///                 sunset, night, rain) and from the causeway's foot
    ///   Charlotte     the biggest lake, the Catawba, a creek: noon and night
    ///   test          a site built 6 km off the ChimneyRock stage: a calm lake
    ///                 under a ridge and a rocky river running into it (the
    ///                 vertex contract: depth, white water, flow) - STAGED
    /// PSX_WATERSHOT_SETS picks (default emerald,langston,charlotte,test).
    /// [WaterShot] lines: each frame, and TIMING per tier (the camera's render
    /// with the water shown vs hidden, drained per render, medians) at
    /// PSX_WATERSHOT_TIME_LINES (default 1440).
    /// -executeMethod PSXRacing.EditorTools.WaterShots.Run
    /// </summary>
    public static class WaterShots
    {
        /// <summary>The editor's tools render through the same per-camera push
        /// as the game: hook it on every domain reload.</summary>
        [InitializeOnLoadMethod]
        static void Hook() => WaterLook.EnsureHook();

        static string dir;
        static int width, lines, timeLines, frames;
        static string tag;

        public static void Run()
        {
            lines = Mathf.Clamp(EnvInt("PSX_WATERSHOT_LINES", 1080), 240, 2160);
            width = Even(Mathf.RoundToInt(lines * 16f / 9f));
            timeLines = Mathf.Clamp(EnvInt("PSX_WATERSHOT_TIME_LINES", 1440), 240, 2160);
            frames = Mathf.Max(6, EnvInt("PSX_WATERSHOT_FRAMES", 30));
            tag = WaterLook.On ? "hd" : "base";
            dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "water");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*_" + tag + ".png")) File.Delete(f);
            WaterLook.EnsureHook();
            Debug.Log($"[WaterShot] {tag}: graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}), {width}x{lines}, " +
                      $"keyword {(Shader.IsKeywordEnabled(WaterLook.Keyword) ? "ON" : "off")}, pipeline {GraphicsSettings.currentRenderPipeline?.name}");
            string sets = Env("PSX_WATERSHOT_SETS", "emerald,langston,charlotte,test");
            int oldWeather = RaceHandoff.WeatherOverride;
            var keepPixels = PSXQuality.Current;
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                foreach (var raw in sets.Split(','))
                {
                    string set = raw.Trim().ToLowerInvariant();
                    try
                    {
                        if (set == "emerald") Emerald();
                        else if (set == "langston") Langston();
                        else if (set == "charlotte") Charlotte();
                        else if (set == "test") TestSite();
                        else if (set == "timing") Timing();
                    }
                    catch (System.Exception e) { Debug.LogError("[WaterShot] " + set + ": " + e); }
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                PSXQuality.Current = keepPixels;
                WaterLook.ForceTier = -1; WaterLook.ForceCamera = null; WaterLook.ForceWind = null; WaterLook.ForceRain = -1f;
                WetReflections.PreviewSet(null, -1);
                WaterLook.Invalidate();
            }
            Debug.Log("[WaterShot] DONE " + tag);
        }

        // ------------------------------------------------------------------
        //  Bogue Banks
        // ------------------------------------------------------------------

        static void Emerald()
        {
            if (!OpenStage("EmeraldIsle", out var cam, out var sun, out var globals, out var path)) return;
            var sea = GameObject.Find("Sea")?.GetComponent<Renderer>();
            if (sea == null) { Debug.LogError("[WaterShot] EmeraldIsle: no Sea"); return; }
            LogMesh(sea);
            ApplyLook(sea, PSXRacingBuilder.WaterPreset.Sea);
            float seaY = sea.bounds.center.y;
            // The open ocean: the station whose land ends soonest to the south
            // (-z, the Atlantic), shot from the beach 25 m short of the water.
            int bestI = -1; float bestD = 1e9f; Vector3 edge = Vector3.zero;
            for (int i = 5; i < path.Count - 5; i += 6)
            {
                Vector3 pt = path.GetPoint(i);
                for (float m = 30f; m < 900f; m += 10f)
                {
                    Vector3 probe = pt + Vector3.back * m + Vector3.up * 80f;
                    // no collider on the sea: land ends where the ray first
                    // lands below it (the seabed's collider, 4 m down)
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hh, 200f)) continue;
                    if (hh.point.y > seaY + 0.05f) continue;
                    if (m < bestD) { bestD = m; bestI = i; edge = pt + Vector3.back * m; }
                    break;
                }
            }
            if (bestI < 0) { Debug.LogError("[WaterShot] EmeraldIsle: no beach"); return; }
            Vector3 beach = edge + Vector3.forward * 25f;
            beach.y = Ground(beach, seaY + 30f) + 1.7f;
            Debug.Log($"[WaterShot] EmeraldIsle beach {bestD:0} m south of wp {bestI}, eye {beach} ({beach.y - seaY:0.0} m over the sea)");
            foreach (var (hour, rain) in new[] { ("NOON", false), ("SUNSET", false), ("NIGHT", false), ("AFTERNOON", true) })
            {
                SetHour(hour, rain, sun, globals);
                Shoot(cam, $"emerald_surf_{(rain ? "rain" : hour.ToLower())}", beach, Vector3.back, -5f);
            }
            SetHour("SUNSET", false, sun, globals);
            Vector3 toSun = -sun.transform.forward; toSun.y = 0f;
            if (toSun.sqrMagnitude > 1e-4f)
                Shoot(cam, "emerald_surf_sunward_sunset", beach, (Vector3.back + toSun.normalized * 0.9f).normalized, -4f);
            SetHour("NOON", false, sun, globals);
            TimeTiers(cam, "emerald_surf", beach, Vector3.back, -5f, new[] { sea });
        }

        static void Langston()
        {
            if (!OpenStage("LangstonBridge", out var cam, out var sun, out var globals, out var path)) return;
            var sea = GameObject.Find("Sea")?.GetComponent<Renderer>();
            if (sea == null) { Debug.LogError("[WaterShot] LangstonBridge: no Sea"); return; }
            LogMesh(sea);
            ApplyLook(sea, PSXRacingBuilder.WaterPreset.Sea);
            float seaY = sea.bounds.center.y;
            int crest = 0;
            for (int i = 0; i < path.Count; i++) if (path.GetPoint(i).y > path.GetPoint(crest).y) crest = i;
            // a third of the way up from the lower end: over the sound, the
            // water near enough to read
            int low = 0;
            for (int i = 0; i < path.Count; i++) if (path.GetPoint(i).y < path.GetPoint(low).y) low = i;
            int mid = (crest * 2 + low) / 3;
            Vector3 at = path.GetPoint(mid);
            Vector3 fwd = path.GetTangent(mid); fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 eye = at + Vector3.up * 3.2f + right * 3.5f;
            Vector3 look = (right * 0.8f + fwd * 0.6f).normalized;
            Debug.Log($"[WaterShot] LangstonBridge deck wp {mid} ({at.y - seaY:0.0} m over the sound)");
            foreach (var (hour, rain) in new[] { ("NOON", false), ("SUNSET", false), ("NIGHT", false), ("AFTERNOON", true) })
            {
                SetHour(hour, rain, sun, globals);
                Shoot(cam, $"langston_sound_{(rain ? "rain" : hour.ToLower())}", eye, look, -13f);
            }
            // the causeway's foot, looking up the bridge with the sound beside it
            Vector3 foot = path.GetPoint(low);
            Vector3 up = (path.GetPoint(crest) - foot); up.y = 0f; up.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, up).normalized;
            Vector3 footEye = foot + Vector3.up * 2.2f + up * 30f;
            SetHour("NOON", false, sun, globals);
            Shoot(cam, "langston_foot_noon", footEye, (up * 0.75f + side * 0.65f).normalized, -6f);
        }

        // ------------------------------------------------------------------
        //  Charlotte
        // ------------------------------------------------------------------

        static void Charlotte()
        {
            const string path = "Assets/PSXRacing/Scenes/Charlotte.unity";
            if (!File.Exists(path)) { Debug.LogError("[WaterShot] no Charlotte scene"); return; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (cam == null) { Debug.LogError("[WaterShot] Charlotte: no PSXCamera"); return; }
            Quiet();
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[WaterShot] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            // the kit's water material as the next kit build will make it
            var cw = PSXRacingBuilder.CityWaterMat();
            Debug.Log("[WaterShot] CityWater: " + (cw != null ? cw.shader.name + ", ripples " + (cw.GetTexture("_RippleTex") != null) : "null"));
            var worldGo = new GameObject("~waterShotWorld");
            var world = worldGo.AddComponent<CityWorld>();
            try
            {
                var picks = new List<(string name, CityMap.Water w)>();
                CityMap.Water lake = null, river = null, creek = null;
                var lakes = new List<CityMap.Water>();
                var creeks = new List<CityMap.Water>();
                var rivers = new List<CityMap.Water>();
                foreach (var w in map.waters)
                {
                    if (w.ravine || w.pts == null || w.pts.Length < 2) continue;
                    string nm = (w.name ?? "").ToLowerInvariant();
                    // the Catawba River is mapped as water areas (and as its lakes)
                    if (nm.Contains("catawba river")) rivers.Add(w);
                    if (w.lake) lakes.Add(w);
                    else if (w.bedY != null && nm.Contains("creek")) creeks.Add(w);
                }
                rivers.Sort((a, b) => ((b.bbMax - b.bbMin).x * (b.bbMax - b.bbMin).y).CompareTo((a.bbMax - a.bbMin).x * (a.bbMax - a.bbMin).y));
                for (int k = 0; k < rivers.Count && k < 8 && river == null; k++)
                    if ((rivers[k].name ?? "").IndexOf("tributary", System.StringComparison.OrdinalIgnoreCase) < 0 && NearRoad(map, rivers[k], true, out _, out _, out _)) river = rivers[k];
                // the biggest lake with a road beside it; the longest creek
                // (Sugar first) with a road over or beside it - only the top
                // few are walked against the roads
                lakes.Sort((a, b) => ((b.bbMax - b.bbMin).x * (b.bbMax - b.bbMin).y).CompareTo((a.bbMax - a.bbMin).x * (a.bbMax - a.bbMin).y));
                creeks.Sort((a, b) => (b.length * ((b.name ?? "").ToLowerInvariant().Contains("sugar") ? 3f : 1f))
                                      .CompareTo(a.length * ((a.name ?? "").ToLowerInvariant().Contains("sugar") ? 3f : 1f)));
                for (int k = 0; k < lakes.Count && k < 6 && lake == null; k++) if (NearRoad(map, lakes[k], false, out _, out _, out _)) lake = lakes[k];
                for (int k = 0; k < creeks.Count && k < 6 && creek == null; k++) if (NearRoad(map, creeks[k], true, out _, out _, out _)) creek = creeks[k];
                Debug.Log($"[WaterShot] Charlotte: {map.waters.Length} waters, {lakes.Count} lakes, {creeks.Count} named creeks; lake '{lake?.name}', river '{river?.name}', creek '{creek?.name}'");
                // Lake Wylie IS the Catawba River, dammed: with no river-named
                // water a road crosses, the Catawba is shot from a bridge over it
                if (river == null && lake != null && (lake.name ?? "").Contains("Wylie")) river = lake;
                if (lake != null) picks.Add(("charlotte_lake", lake));
                if (river != null) picks.Add(("charlotte_catawba", river));
                if (creek != null) picks.Add(("charlotte_creek", creek));
                foreach (var (name, w) in picks)
                {
                    bool overOk = !w.lake || name == "charlotte_catawba";
                    if (!NearRoad(map, w, overOk, out Vector3 eye, out Vector3 look, out string why))
                    { Debug.LogError("[WaterShot] " + name + ": no road near '" + w.name + "'"); continue; }
                    // from a bridge deck the parapet hides the water at a
                    // driver's eye: stand higher and look down over it
                    bool fromBridge = why.StartsWith("from a bridge");
                    if (fromBridge) eye += Vector3.up * 3.0f;
                    float pitch = fromBridge ? -24f : -7f;
                    Debug.Log($"[WaterShot] {name}: '{w.name}' ({(w.lake ? "lake" : w.bedY != null ? "creek" : "river")}, {w.width:0} m wide) {why}, eye {eye}");
                    world.EnsureRing(eye, world.ring);
                    foreach (var hour in new[] { "NOON", "NIGHT" })
                    {
                        SetHour(hour, false, sun, globals);
                        world.EnsureRing(eye, world.ring);
                        Shoot(cam, name + "_" + hour.ToLower(), eye, look, pitch);
                    }
                }
                if (lake != null && NearRoad(map, lake, false, out Vector3 le, out Vector3 ll, out _))
                {
                    SetHour("NOON", false, sun, globals);
                    world.EnsureRing(le, world.ring);
                    var waters = new List<Renderer>();
                    foreach (var r in worldGo.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "PSX/Water") waters.Add(r);
                    TimeTiers(cam, "charlotte_lake", le, ll, -7f, waters.ToArray());
                }
            }
            finally { Object.DestroyImmediate(worldGo); }
        }

        /// <summary>A road point beside (or over) the water and the look toward
        /// it. overOk: a bridge over it is the better view (rivers, creeks).</summary>
        static bool NearRoad(CityMap map, CityMap.Water w, bool overOk, out Vector3 eye, out Vector3 look, out string why)
        {
            eye = look = Vector3.zero; why = "";
            float best = float.MaxValue;
            Vector2 c = (w.bbMin + w.bbMax) * 0.5f;
            float reach = Mathf.Max((w.bbMax - w.bbMin).magnitude * 0.5f, 50f) + 200f;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.length < 20f) continue;
                var pa = e.PointAt(0f); var pb = e.PointAt(e.length);
                if (Vector2.Distance(pa, c) > reach + e.length && Vector2.Distance(pb, c) > reach + e.length) continue;
                for (float s = 5f; s < e.length - 5f; s += 8f)
                {
                    var p = e.PointAt(s);
                    if (Vector2.Distance(p, c) > reach) continue;
                    float d = WaterDistance(w, p, out Vector2 nearest);
                    bool elevated = e.ElevatedAt(s);
                    float score;
                    if (overOk && elevated && d < -1f) score = -1000f + Mathf.Abs(d);            // on a bridge over it
                    else if (!elevated && d > 12f && d < 70f) score = d;                        // beside it
                    else continue;
                    if (score >= best) continue;
                    best = score;
                    float y = e.YAt(s);
                    var t2 = e.TangentAt(s).normalized;
                    Vector3 fwd = new Vector3(t2.x, 0f, t2.y);
                    if (score < 0f)
                    {
                        // over the water: look along it (its line at the crossing)
                        Vector2 along = WaterTangent(w, nearest);
                        look = new Vector3(along.x, 0f, along.y).normalized;
                        eye = new Vector3(p.x, y + 1.6f, p.y) + Vector3.Cross(Vector3.up, fwd) * 0f;
                        why = $"from a bridge on '{e.name}'";
                    }
                    else
                    {
                        Vector2 to = (nearest - p).normalized;
                        look = new Vector3(to.x, 0f, to.y);
                        eye = new Vector3(p.x, y + 1.6f, p.y);
                        why = $"from '{e.name}' {d:0} m off the water";
                    }
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>Signed distance from p to the water's edge (negative inside).</summary>
        static float WaterDistance(CityMap.Water w, Vector2 p, out Vector2 nearest)
        {
            nearest = p;
            float dmin = float.MaxValue;
            int n = w.pts.Length;
            int segs = w.lake ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = w.pts[i], b = w.pts[(i + 1) % n], d = b - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                var q = a + d * t;
                float dd = Vector2.Distance(p, q);
                if (dd < dmin) { dmin = dd; nearest = q; }
            }
            if (w.lake) return CityMap.LakeContains(w, p) ? -dmin : dmin;
            return dmin - w.width * 0.5f;
        }

        static Vector2 WaterTangent(CityMap.Water w, Vector2 at)
        {
            float dmin = float.MaxValue; Vector2 tan = Vector2.right;
            for (int i = 0; i + 1 < w.pts.Length; i++)
            {
                Vector2 a = w.pts[i], b = w.pts[i + 1], d = b - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(at - a, d) / L2);
                float dd = Vector2.Distance(at, a + d * t);
                if (dd < dmin) { dmin = dd; tan = d.normalized; }
            }
            return tan;
        }

        // ------------------------------------------------------------------
        //  The test site: a calm lake under a ridge, a rocky river into it
        // ------------------------------------------------------------------

        static void TestSite()
        {
            if (!OpenStage("ChimneyRock", out var cam, out var sun, out var globals, out var path)) return;
            var site = WaterTestSite.Build(path);
            if (site == null) return;
            noSunMap = true;
            foreach (var (hour, rain) in new[] { ("NOON", false), ("SUNSET", false), ("NIGHT", false), ("AFTERNOON", true) })
            {
                SetHour(hour, rain, sun, globals);
                Shoot(cam, $"test_lake_{(rain ? "rain" : hour.ToLower())}", site.lakeEye, site.lakeLook, -2.5f);
            }
            foreach (var hour in new[] { "NOON", "SUNSET" })
            {
                SetHour(hour, false, sun, globals);
                Shoot(cam, "test_river_" + hour.ToLower(), site.riverEye, site.riverLook, -11f);
            }
            SetHour("NOON", false, sun, globals);
            Shoot(cam, "test_mouth_noon", site.mouthEye, site.mouthLook, -16f);
            // the calm: a still morning, no wind at all (the mirror at its best)
            WaterLook.ForceWind = Vector2.zero;
            SetHour("MORNING", false, sun, globals);
            Shoot(cam, "test_lake_calm_morning", site.lakeEye, site.lakeLook, -2.5f);
            WaterLook.ForceWind = null;
            SetHour("NOON", false, sun, globals);
            TimeTiers(cam, "test_lake", site.lakeEye, site.lakeLook, -2.5f, site.water);
            noSunMap = false;
            Object.DestroyImmediate(site.root);
        }

        // ------------------------------------------------------------------
        //  The cost, on its own: an empty scene (the last hour's globals
        //  carry over), a ground plane 3 m under a 400 m lake and a wall of
        //  far shore for the mirror to meet; the water shown and hidden in
        //  turn at 1440p, drained per render, per tier. Run it last.
        // ------------------------------------------------------------------

        static void Timing()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("WaterTimingCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 2500f;
            cam.allowHDR = true; cam.allowMSAA = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.80f);
            var lit = new Material(Shader.Find("PSX/Lit")) { color = new Color(0.35f, 0.33f, 0.25f) };
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0f, -3f, 0f);
            ground.transform.localScale = new Vector3(90f, 1f, 90f);
            ground.GetComponent<Renderer>().sharedMaterial = lit;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 25f, 260f);
            wall.transform.localScale = new Vector3(700f, 60f, 10f);
            wall.GetComponent<Renderer>().sharedMaterial = lit;
            var m = new Material(Shader.Find("PSX/Water")) { name = "TimingLake" };
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PSXRacing/Art/Water/water_2.png");
            m.SetTexture("_DeepTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PSXRacing/Art/Water/water_1.png"));
            PSXRacingBuilder.WaterPreset.Lake.Apply(m);
            var v = new List<Vector3>(); var c = new List<Color32>(); var uv = new List<Vector2>(); var tris = new List<int>();
            int n = 100; float cell = 4f;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    v.Add(new Vector3(-200f + i * cell, 0f, -200f + j * cell + 60f));
                    c.Add(new Color32((byte)Mathf.RoundToInt((0.5f + 3f / 24f) * 255f), 0, 0, 255));
                    uv.Add(new Vector2(i * cell / 26f, j * cell / 26f));
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i;
                    tris.Add(a); tris.Add(a + n + 1); tris.Add(a + n + 2);
                    tris.Add(a); tris.Add(a + n + 2); tris.Add(a + 1);
                }
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var wgo = new GameObject("TimingWater");
            wgo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var wr = wgo.AddComponent<MeshRenderer>();
            wr.sharedMaterial = m;
            TimeTiers(cam, "synthetic_lake", new Vector3(0f, 5f, -150f), Vector3.forward, -6f, new Renderer[] { wr });
        }

        // ------------------------------------------------------------------
        //  Shared
        // ------------------------------------------------------------------

        static bool OpenStage(string id, out Camera cam, out Light sun, out PSXGlobals globals, out TrackPath path)
        {
            cam = null; sun = null; globals = null; path = null;
            TrackCatalog.TrackDef def = null;
            foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
            if (def == null || !PSXScreenshotTool.Open(def, out cam, out _)) { Debug.LogError("[WaterShot] cannot open " + id); return false; }
            sun = GameObject.Find("Sun")?.GetComponent<Light>();
            globals = Object.FindAnyObjectByType<PSXGlobals>();
            path = Object.FindAnyObjectByType<TrackPath>();
            if (sun == null || path == null) { Debug.LogError("[WaterShot] " + id + ": no sun or path"); return false; }
            Quiet();
            return true;
        }

        /// <summary>No HUD, no cabin, no car paint probe in the frames.</summary>
        static void Quiet()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            foreach (var cv in Object.FindObjectsByType<CockpitView>()) cv.PreviewShow(false);
            CarReflections.Off();
            SunGlareFx.PreviewSet(null, 0f, 0f, true);
            var car = GameObject.Find("RX-7 Player");
            if (car != null) car.SetActive(false);
        }

        static void SetHour(string hourName, bool rain, Light sun, PSXGlobals globals)
        {
            RaceHandoff.WeatherOverride = rain ? (int)Weather.Rain : -1;
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName, System.StringComparison.OrdinalIgnoreCase));
            if (hour < 0) hour = TimeOfDay.Noon;
            Seasons.Invalidate();
            TimeOfDay.Apply(hour, sun);
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            WaterLook.Invalidate();
            WetReflections.TagAll();
            // the staged test site lies outside the stage's sun-shadow map
            // (its land drew black under it): the map is off there
            if (noSunMap && sun != null) SunShadows.Configure(0f, 0f, -sun.transform.forward);
        }

        static bool noSunMap;

        static void Shoot(Camera cam, string name, Vector3 eye, Vector3 flatDir, float pitchDeg)
        {
            var rot = Quaternion.LookRotation(flatDir) * Quaternion.Euler(-pitchDeg, 0f, 0f);
            cam.transform.SetPositionAndRotation(eye, rot);
            WaterLook.ForceCamera = cam; WaterLook.ForceTier = 2;
            WetReflections.PreviewSet(cam, 1);
            var tex = Render(cam, width, lines);
            File.WriteAllBytes(Path.Combine(dir, name + "_" + tag + ".png"), tex.EncodeToPNG());
            string opts = cam.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData acd)
                ? $"{acd.renderType} depth {acd.requiresDepthOption} colour {acd.requiresColorOption}" : "no camera data";
            Debug.Log($"[WaterShot] {name}_{tag}: eye {eye} wind {Shader.GetGlobalVector("_PSXWaterWind")} rain {Shader.GetGlobalFloat("_PSXWaterRain"):0.00} " +
                      $"wet {Shader.GetGlobalFloat("_PSXWetness"):0.00} tier {Shader.GetGlobalVector("_PSXWaterTier")} copies {WaterLook.SceneCopies(cam)} ({opts})");
            Object.DestroyImmediate(tex);
            if (System.Environment.GetEnvironmentVariable("PSX_WATERSHOT_DEBUG") == "1" && WaterLook.On)
            {
                for (int mode = 1; mode <= 3; mode++)
                {
                    Shader.SetGlobalFloat("_PSXWaterDebug", mode);
                    var dbg = Render(cam, width, lines);
                    File.WriteAllBytes(Path.Combine(dir, name + "_dbg" + mode + "_" + tag + ".png"), dbg.EncodeToPNG());
                    Object.DestroyImmediate(dbg);
                }
                Shader.SetGlobalFloat("_PSXWaterDebug", 0f);
            }
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            Submit(cam, rt);
            var shown = PSXScreenshotTool.Dithered(rt);
            var prev = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static void Submit(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            // a render request may not raise beginCameraRendering: push the
            // water's globals for this camera ourselves (idempotent)
            WaterLook.Push(cam);
            PSXTexDecode.StampAll();
            RenderPipeline.SubmitRenderRequest(cam, request);
        }

        /// <summary>The water's cost per tier: the same pose rendered with the
        /// water shown and hidden, alternating, drained per render (the GPU's
        /// work inside the clock), medians.</summary>
        static void TimeTiers(Camera cam, string name, Vector3 eye, Vector3 flatDir, float pitchDeg, Renderer[] water)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_WATERSHOT_NOTIME") == "1") return;
            // a stage's own frame on a shared GPU swings by more than the water
            // costs (tens of ms render to render): only the synthetic scene is
            // timed unless asked
            if (name != "synthetic_lake" && System.Environment.GetEnvironmentVariable("PSX_WATERSHOT_STAGETIME") != "1") return;
            if (water == null || water.Length == 0) { Debug.LogWarning("[WaterShot] TIMING " + name + ": no water renderers"); return; }
            int tw = Even(Mathf.RoundToInt(timeLines * 16f / 9f));
            var rot = Quaternion.LookRotation(flatDir) * Quaternion.Euler(-pitchDeg, 0f, 0f);
            cam.transform.SetPositionAndRotation(eye, rot);
            var rt = new RenderTexture(tw, timeLines, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            int[] tiers = WaterLook.On ? new[] { 0, 1, 2 } : new[] { 2 };
            WetReflections.PreviewSet(cam, 1);
            // how much of the frame is water: the pixels that change when it hides
            {
                WaterLook.ForceCamera = cam; WaterLook.ForceTier = 2;
                Show(water, true);
                var a = ReadBack(cam, rt);
                Show(water, false);
                var b = ReadBack(cam, rt);
                Show(water, true);
                int changed = 0;
                for (int k = 0; k < a.Length; k++)
                    if (Mathf.Abs(a[k].r - b[k].r) + Mathf.Abs(a[k].g - b[k].g) + Mathf.Abs(a[k].b - b[k].b) > 6) changed++;
                Debug.Log($"[WaterShot] TIMING {name}: the water covers {100f * changed / Mathf.Max(a.Length, 1):0.0}% of the frame");
            }
            foreach (int tier in tiers)
            {
                WaterLook.ForceCamera = cam; WaterLook.ForceTier = tier;
                for (int i = 0; i < 6; i++) { Show(water, (i & 1) == 0); Submit(cam, rt); Drain(rt, one); }
                var shown = new List<double>(); var hidden = new List<double>();
                var sw = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < 2 * frames; i++)
                {
                    bool on = (i & 1) == 0;
                    Show(water, on);
                    sw.Restart();
                    Submit(cam, rt);
                    Drain(rt, one);
                    sw.Stop();
                    (on ? shown : hidden).Add(sw.Elapsed.TotalMilliseconds);
                }
                Show(water, true);
                double a = Median(shown), b = Median(hidden);
                Debug.Log($"[WaterShot] TIMING {name} {tag} tier {tier} {tw}x{timeLines}: water shown {a:0.00} ms, hidden {b:0.00} ms -> the water costs {a - b:0.00} ms");
            }
            WaterLook.ForceTier = 2;
            Object.DestroyImmediate(one);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static void Show(Renderer[] rs, bool on) { foreach (var r in rs) if (r != null) r.enabled = on; }

        static Color32[] ReadBack(Camera cam, RenderTexture rt)
        {
            Submit(cam, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        static void Drain(RenderTexture rt, Texture2D one)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = prev;
        }

        static double Median(List<double> v)
        {
            v.Sort();
            return v.Count == 0 ? 0.0 : (v.Count & 1) == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        /// <summary>A sea built before this round has no ripple sheet on its
        /// material: shoot it with a runtime copy carrying what MakeWaterMat
        /// now sets (the scene is never saved; the asset is untouched).</summary>
        static void ApplyLook(Renderer r, PSXRacingBuilder.WaterPreset look)
        {
            var src = r.sharedMaterial;
            if (src == null || !src.HasProperty("_RippleTex")) return;
            if (src.GetTexture("_RippleTex") != null) return;
            var m = new Material(src) { name = src.name + " (as MakeWaterMat makes it)" };
            look.Apply(m);
            r.sharedMaterial = m;
            Debug.Log("[WaterShot] " + r.name + ": material built before this round - shot with the look MakeWaterMat now sets");
        }

        static void LogMesh(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null) return;
            Debug.Log($"[WaterShot] {r.name}: {m.vertexCount} verts, colour {m.HasVertexAttribute(VertexAttribute.Color)}, uv2 {m.HasVertexAttribute(VertexAttribute.TexCoord1)}, " +
                      $"material {r.sharedMaterial?.name} queue {r.sharedMaterial?.renderQueue}, ripples {(r.sharedMaterial != null && r.sharedMaterial.HasProperty("_RippleTex") && r.sharedMaterial.GetTexture("_RippleTex") != null)}");
        }

        static float Ground(Vector3 at, float top)
        {
            if (Physics.Raycast(new Vector3(at.x, top + 60f, at.z), Vector3.down, out var hit, 400f)) return hit.point.y;
            return top - 30f;
        }

        static int Even(int w) => (w & 1) == 1 ? w + 1 : w;

        static string Env(string name, string fallback)
        {
            string s = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        static int EnvInt(string name, int fallback) =>
            int.TryParse(System.Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;
    }

    /// <summary>
    /// THE WATER TEST SITE (WaterShots' "test" set): built in memory, never
    /// saved, 6 km off a mountain stage so nothing of the stage stands in it
    /// (its sky, sun and fog are the stage's). A lake in a bowl under a ridge
    /// that rises 260 m, its shores planted with the stage's own trees; a river
    /// running down a 4.5% valley into it over boulders. Every water vertex
    /// carries the contract: colour r depth, g 0, b white water; uv2 flow m/s.
    /// </summary>
    public static class WaterTestSite
    {
        public class Site
        {
            public GameObject root;
            public Renderer[] water;
            public Vector3 lakeEye, lakeLook, riverEye, riverLook, mouthEye, mouthLook;
        }

        const float LakeRX = 170f, LakeRZ = 110f, LakeDepth = 9f;
        const float HalfW = 6f, RiverDrop = 0.045f;
        static Vector3 origin;
        static List<Vector2> river;     // the river's line, upstream -> the lake
        static List<float> riverS;
        static float riverLen;
        static readonly List<Vector4> boulders = new List<Vector4>();   // x, z, radius, top over the water

        public static Site Build(TrackPath path)
        {
            // 6 km off the route's start, at the route's height
            Vector3 start = path.GetPoint(0);
            origin = new Vector3(start.x + 6000f, start.y, start.z + 1500f);
            var root = new GameObject("~WaterTestSite");
            root.hideFlags = HideFlags.DontSave;
            // what the stage dresses its land with (logged, to choose from)
            var uses = new Dictionary<Material, int>();
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>())
                foreach (var m in mr.sharedMaterials)
                    if (m != null) uses[m] = (uses.TryGetValue(m, out int u) ? u : 0) + 1;
            var top = new List<KeyValuePair<Material, int>>(uses);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int k = 0; k < top.Count && k < 14; k++)
                Debug.Log($"[WaterShot] stage material {top[k].Key.name} x{top[k].Value}: {top[k].Key.shader?.name} tex {top[k].Key.mainTexture?.name}");
            BuildRiverLine();
            PlaceBoulders();

            // the far land's material for all of it: the near one reads its baked
            // light through uv2, which this land does not have (it drew black)
            var farMat = FindByNameEnd("_GroundFar") ?? FindByNameEnd("_Ground") ?? FindByTexture(new[] { "turf", "grass" });
            var groundMat = farMat;
            var rockMat = FindByTexture(new[] { "rock" }) ?? groundMat;
            if (groundMat == null) { Debug.LogError("[WaterShot] test site: no ground material in the stage"); return null; }
            Debug.Log($"[WaterShot] test site at {origin}: ground {groundMat.name}, far {farMat?.name}, rock {rockMat?.name}");

            // near ground (3 m cells, 720 m square) and far ground (25 m, 4 km)
            Grid(root.transform, "TestNear", -360f, 360f, 3f, groundMat, 11f, null);
            Grid(root.transform, "TestFar", -2000f, 2000f, 25f, farMat, 60f, new Rect(-360f, -360f, 720f, 720f));
            // boulders
            foreach (var b in boulders) Boulder(root.transform, b, rockMat);
            // the stage's own trees on the shores and up the ridge
            Trees(root.transform);

            var lakeMat = WaterMat("TestLake", PSXRacingBuilder.WaterPreset.Lake);
            var riverMat = WaterMat("TestRiver", PSXRacingBuilder.WaterPreset.River);
            var lake = LakeMesh(root.transform, lakeMat);
            var riv = RiverMesh(root.transform, riverMat);

            var site = new Site { root = root, water = new[] { lake, riv } };
            // the lake from its near shore at a car's eye, across to the ridge
            site.lakeEye = origin + new Vector3(30f, 0f, -LakeRZ - 22f);
            site.lakeEye.y = H(site.lakeEye.x - origin.x, site.lakeEye.z - origin.z) + origin.y + 1.3f;
            site.lakeLook = new Vector3(0.05f, 0f, 1f).normalized;
            // the river from its bank, looking upstream over the boulders
            float sEye = riverLen * 0.62f;
            Vector2 rp = RiverAt(sEye, out Vector2 rt);
            Vector2 bank = rp + new Vector2(-rt.y, rt.x) * (HalfW + 1.5f);
            site.riverEye = origin + new Vector3(bank.x, 0f, bank.y);
            site.riverEye.y = Mathf.Max(H(bank.x, bank.y) + 1.6f, RiverSurface(sEye) + 5.0f) + origin.y;
            Vector2 up = -rt;
            site.riverLook = new Vector3(up.x * 0.85f - rt.y * 0.0f, 0f, up.y * 0.85f).normalized;
            Vector2 aim = RiverAt(sEye - 30f, out _) - bank;
            site.riverLook = new Vector3(aim.x, 0f, aim.y).normalized;
            // where the river meets the lake, from above the bank
            Vector2 mp = RiverAt(riverLen - 30f, out Vector2 mt);
            Vector2 mb = mp + new Vector2(mt.y, -mt.x) * (HalfW + 14f);
            site.mouthEye = origin + new Vector3(mb.x, 0f, mb.y);
            site.mouthEye.y = H(mb.x, mb.y) + origin.y + 3.5f;
            Vector2 ma = RiverAt(riverLen - 5f, out _) - mb;
            site.mouthLook = new Vector3(ma.x, 0f, ma.y).normalized;
            Debug.Log($"[WaterShot] test site: river {riverLen:0} m, {boulders.Count} boulders; eyes lake {site.lakeEye} river {site.riverEye} mouth {site.mouthEye}");
            return site;
        }

        // ---- the land ----

        static float Ridge(float x, float z)
        {
            float rz = z - 640f - 60f * Mathf.Sin(x / 210f);
            float ridge = 270f * Mathf.Exp(-(rz * rz) / (230f * 230f)) * (0.85f + 0.15f * Mathf.Sin(x / 97f + 1.3f));
            float hills = 22f * Mathf.Sin(x / 83f) * Mathf.Sin(z / 71f + 0.7f) + 9f * Mathf.Sin(x / 31f + z / 43f);
            return ridge + hills;
        }

        /// <summary>The ground, local metres (0 = the lake's surface).</summary>
        static float H(float x, float z)
        {
            float ex = x / LakeRX, ez = z / LakeRZ;
            float r = Mathf.Sqrt(ex * ex + ez * ez);
            float bowl = r < 1f ? -LakeDepth * (1f - r * r) * (1f - r * r * 0.15f) - 0.15f : (r - 1f) * 14f + 0.25f;
            float h = bowl + Mathf.Max(0f, Ridge(x, z)) * Mathf.Clamp01((r - 1.15f) * 0.9f);
            // the river's valley: a V cut down to its banks, the bed under the water
            float d = RiverDistance(new Vector2(x, z), out float s);
            if (d < 150f)
            {
                float sy = RiverSurface(s);
                float e = Mathf.Max(d - HalfW, 0f);
                float valley = sy + 0.45f + 0.28f * e + 0.0016f * e * e;
                float u = Mathf.Clamp01(d / HalfW);
                float bed = sy - 0.85f * (1f - u * u) - 0.1f;
                // the banks stand over the water until the river reaches the lake
                float mouth = Mathf.Clamp01((riverLen - s) / 25f);
                float carved = d < HalfW ? bed : Mathf.Max(Mathf.Min(h, valley), Mathf.Lerp(Mathf.Min(h, 0.2f), sy + 0.45f, mouth));
                h = Mathf.Lerp(carved, h, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 110f) / 40f)));
                if (s > riverLen - 25f && r < 1.05f) h = Mathf.Min(h, bowl);
            }
            return h;
        }

        static void BuildRiverLine()
        {
            river = new List<Vector2>(); riverS = new List<float>();
            // from up the valley (-x, -z) down to the lake's west shore
            Vector2 a = new Vector2(-560f, -300f), b = new Vector2(-LakeRX * 0.92f, -LakeRZ * 0.35f);
            int n = 80;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector2 p = Vector2.Lerp(a, b, t);
                Vector2 dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x);
                p += side * (22f * Mathf.Sin(t * 9.5f) * (1f - t * 0.6f));
                river.Add(p);
            }
            riverS.Add(0f);
            for (int i = 1; i < river.Count; i++) riverS.Add(riverS[i - 1] + Vector2.Distance(river[i - 1], river[i]));
            riverLen = riverS[riverS.Count - 1];
        }

        static float RiverSurface(float s) => (riverLen - s) * RiverDrop + 0.0f;

        static Vector2 RiverAt(float s, out Vector2 tangent)
        {
            s = Mathf.Clamp(s, 0f, riverLen);
            int i = 1;
            while (i < riverS.Count - 1 && riverS[i] < s) i++;
            float t = Mathf.InverseLerp(riverS[i - 1], riverS[i], s);
            tangent = (river[i] - river[i - 1]).normalized;
            return Vector2.Lerp(river[i - 1], river[i], t);
        }

        static float RiverDistance(Vector2 p, out float s)
        {
            float best = float.MaxValue; s = 0f;
            for (int i = 0; i + 1 < river.Count; i++)
            {
                Vector2 a = river[i], d = river[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / L2);
                float dd = Vector2.Distance(p, a + d * t);
                if (dd < best) { best = dd; s = riverS[i] + Mathf.Sqrt(L2) * t; }
            }
            return best;
        }

        static void PlaceBoulders()
        {
            boulders.Clear();
            var rng = new System.Random(64);
            for (int k = 0; k < 14; k++)
            {
                float s = riverLen * (0.25f + 0.62f * k / 14f) + (float)rng.NextDouble() * 6f;
                Vector2 p = RiverAt(s, out Vector2 t);
                float off = ((float)rng.NextDouble() * 2f - 1f) * (HalfW - 1.6f);
                p += new Vector2(-t.y, t.x) * off;
                float r = 0.7f + (float)rng.NextDouble() * 0.9f;
                boulders.Add(new Vector4(p.x, p.y, r, 0.15f + (float)rng.NextDouble() * 0.6f));
            }
        }

        static void Grid(Transform parent, string name, float lo, float hi, float cell, Material mat, float tile, Rect? hole)
        {
            int n = Mathf.CeilToInt((hi - lo) / cell);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = lo + i * cell, z = lo + j * cell;
                    float h = H(x, z);
                    // nothing of the land's surface may hide under the lake plane
                    v.Add(new Vector3(x, h, z) + origin);
                    uv.Add(new Vector2((x + origin.x) / tile, (z + origin.z) / tile));
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float cx = lo + (i + 0.5f) * cell, cz = lo + (j + 0.5f) * cell;
                    if (hole.HasValue && hole.Value.Contains(new Vector2(cx, cz))) continue;
                    int a = j * (n + 1) + i;
                    tris.Add(a); tris.Add(a + n + 1); tris.Add(a + n + 2);
                    tris.Add(a); tris.Add(a + n + 2); tris.Add(a + 1);
                }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var cols = new Color32[v.Count];
            for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(255, 255, 255, 255);
            mesh.colors32 = cols;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var gr = go.AddComponent<MeshRenderer>();
            gr.sharedMaterial = mat;
            // land does not cast into the sun map (self-shadowed, it drew black)
            gr.shadowCastingMode = ShadowCastingMode.Off;
            SunShadows.Exclude(go);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static void Boulder(Transform parent, Vector4 b, Material mat)
        {
            float sy = 0f;
            RiverDistance(new Vector2(b.x, b.y), out float s);
            sy = RiverSurface(s);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TestBoulder";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            float r = b.z;
            // squashed and turned, its top b.w over the water
            go.transform.localScale = new Vector3(r * 2.2f, r * 1.3f, r * 1.7f);
            go.transform.rotation = Quaternion.Euler(0f, b.x * 7.3f % 360f, 0f);
            go.transform.position = origin + new Vector3(b.x, sy + b.w - r * 0.65f, b.y);
            var mf = go.GetComponent<MeshFilter>();
            var m = Object.Instantiate(mf.sharedMesh);
            var vs = m.vertices;
            for (int k = 0; k < vs.Length; k++)
            {
                var p = vs[k];
                float lump = 1f + 0.18f * Mathf.Sin(p.x * 9.1f + b.x) * Mathf.Sin(p.y * 7.7f) + 0.1f * Mathf.Sin(p.z * 13.3f + b.y);
                vs[k] = p * lump;
            }
            m.vertices = vs; m.RecalculateNormals();
            var cols = new Color32[vs.Length];
            for (int k = 0; k < cols.Length; k++) cols[k] = new Color32(255, 255, 255, 255);
            m.colors32 = cols;
            var uvs = new Vector2[vs.Length];
            for (int k = 0; k < vs.Length; k++) uvs[k] = new Vector2(vs[k].x + vs[k].y, vs[k].z + vs[k].y) * 1.5f;
            m.uv = uvs;
            mf.sharedMesh = m;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void Trees(Transform parent)
        {
            // prototypes: the stage's own trees (mesh trees if it has them)
            var protos = new List<GameObject>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                if (protos.Count >= 8) break;
                var go = r.gameObject;
                string n = go.name.ToLowerInvariant();
                if (!(n.Contains("tree") || n.Contains("pine") || n.Contains("oak") || n.Contains("maple") || n.Contains("beech"))) continue;
                var b = r.bounds;
                if (b.size.y < 6f || b.size.y > 40f || b.size.x > 25f) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount > 40000) continue;
                // a tree stands on its own pivot (a chunk of baked cards does not)
                var mb = mf.sharedMesh.bounds;
                if (Mathf.Abs(mb.center.x) > 3f || Mathf.Abs(mb.center.z) > 3f || mb.min.y < -2f || mb.min.y > 3f) continue;
                var top = go.transform.parent != null && go.transform.parent.GetComponent<LODGroup>() != null ? go.transform.parent.gameObject : go;
                if (protos.Contains(top)) continue;
                protos.Add(top);
            }
            Debug.Log("[WaterShot] test site: " + protos.Count + " tree prototype(s): " + string.Join(", ", protos.ConvertAll(p => p.name)));
            if (protos.Count == 0) return;
            var rng = new System.Random(5);
            int placed = 0;
            for (int k = 0; k < 2600 && placed < 900; k++)
            {
                float x = (float)(rng.NextDouble() * 1400.0 - 700.0);
                float z = (float)(rng.NextDouble() * 1300.0 - 420.0);
                float ex = x / LakeRX, ez = z / LakeRZ;
                float r = Mathf.Sqrt(ex * ex + ez * ez);
                if (r < 1.12f) continue;                          // not in the lake or on its beach
                if (RiverDistance(new Vector2(x, z), out _) < HalfW + 4f) continue;
                // keep the camera's foreground (the near shore) open
                if (z < -LakeRZ - 8f && z > -LakeRZ - 60f && Mathf.Abs(x - 30f) < 70f) continue;
                float h = H(x, z);
                var proto = protos[rng.Next(protos.Count)];
                var t = Object.Instantiate(proto, parent).transform;
                t.position = origin + new Vector3(x, h - 0.2f, z);
                t.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float sc = 0.85f + (float)rng.NextDouble() * 0.4f;
                t.localScale = proto.transform.lossyScale * sc;
                placed++;
            }
            Debug.Log("[WaterShot] test site: " + placed + " trees");
        }

        /// <summary>The stage's own material whose name ends so ("_Ground" =
        /// its near ground, "_GroundFar" its far land).</summary>
        static Material FindByNameEnd(string end)
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.EndsWith(end, System.StringComparison.Ordinal))
                    { Debug.Log($"[WaterShot] test site material '{end}': {m.name} ({m.mainTexture?.name})"); return m; }
            return null;
        }

        /// <summary>A PSX/Lit material of the stage whose main texture's name
        /// holds one of the keys (logged, so the frame can be judged).</summary>
        static Material FindByTexture(string[] keys)
        {
            var seen = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m) || m.shader == null || m.shader.name != "PSX/Lit" || m.mainTexture == null) continue;
                    string tn = m.mainTexture.name.ToLowerInvariant();
                    foreach (var k in keys)
                        if (tn.Contains(k)) { Debug.Log($"[WaterShot] test site material for '{k}': {m.name} ({m.mainTexture.name}) on {r.name}"); return m; }
                }
            }
            return null;
        }

        static Material FindMaterial(string[] keys, string shader)
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != shader) continue;
                string n = (r.name + " " + m.name).ToLowerInvariant();
                foreach (var k in keys)
                    if (n.Contains(k.ToLowerInvariant()) && !n.Contains("water") && !n.Contains("sea") && !n.Contains("road")) return m;
            }
            return null;
        }

        static Material WaterMat(string name, PSXRacingBuilder.WaterPreset look)
        {
            var m = new Material(Shader.Find("PSX/Water")) { name = name };
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PSXRacing/Art/Water/water_2.png");
            m.SetTexture("_DeepTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PSXRacing/Art/Water/water_1.png"));
            m.mainTextureScale = new Vector2(1f, 1f);
            m.SetColor("_Color", new Color(0.62f, 0.74f, 0.70f));
            m.SetColor("_SandColor", new Color(0.46f, 0.40f, 0.30f));
            m.SetFloat("_OceanWaves", 0f);
            look.Apply(m);
            return m;
        }

        // ---- the water ----

        static Renderer LakeMesh(Transform parent, Material mat)
        {
            float cell = 4f;
            var v = new List<Vector3>(); var c = new List<Color32>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tris = new List<int>();
            int nx = Mathf.CeilToInt(LakeRX * 2.3f / cell), nz = Mathf.CeilToInt(LakeRZ * 2.3f / cell);
            float x0 = -LakeRX * 1.15f, z0 = -LakeRZ * 1.15f;
            var index = new int[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * cell, z = z0 + j * cell;
                    float depth = -H(x, z);
                    index[j * (nx + 1) + i] = v.Count;
                    v.Add(origin + new Vector3(x, 0f, z));
                    c.Add(new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + depth / 24f) * 255f), 0, 0, 255));
                    uv.Add(new Vector2((x + origin.x) / 26f, (z + origin.z) / 26f));
                    uv2.Add(Vector2.zero);
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i;
                    // a cell that is land at all four corners is not drawn
                    bool land = true;
                    foreach (int q in new[] { a, a + 1, a + nx + 1, a + nx + 2 })
                        if (c[index[q]].r > 120) land = false;
                    if (land) continue;
                    tris.Add(index[a]); tris.Add(index[a + nx + 1]); tris.Add(index[a + nx + 2]);
                    tris.Add(index[a]); tris.Add(index[a + nx + 2]); tris.Add(index[a + 1]);
                }
            return WaterObject(parent, "TestLakeWater", v, c, uv, uv2, tris, mat);
        }

        static Renderer RiverMesh(Transform parent, Material mat)
        {
            var v = new List<Vector3>(); var c = new List<Color32>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tris = new List<int>();
            int across = 7;
            float step = 2f;
            int rows = Mathf.CeilToInt(riverLen / step);
            for (int j = 0; j <= rows; j++)
            {
                float s = Mathf.Min(j * step, riverLen);
                Vector2 p = RiverAt(s, out Vector2 t);
                Vector2 side = new Vector2(-t.y, t.x);
                float sy = RiverSurface(s);
                // the steeper reaches (where the bed drops over rock) run faster and whiter
                float riffle = Mathf.Clamp01(Mathf.Sin(s / 23f) * 1.4f - 0.2f);
                for (int i = 0; i <= across; i++)
                {
                    float u = i / (float)across * 2f - 1f;   // -1..1 across
                    float w = HalfW + 0.8f;
                    Vector2 q = p + side * (u * w);
                    float depth = sy - H(q.x, q.y);
                    // the current: fastest mid-channel, slower near the banks
                    float speed = (1.1f + 1.3f * riffle) * (1f - 0.55f * u * u);
                    // white water: over the riffles and behind each boulder
                    float white = riffle * 0.35f * (1f - 0.5f * u * u);
                    foreach (var b in boulders)
                    {
                        Vector2 rel = q - new Vector2(b.x, b.y);
                        float along = Vector2.Dot(rel, t), cross = Vector2.Dot(rel, side);
                        // a wedge of foam from the boulder downstream, and a cushion on its upstream face
                        float wake = along > -b.z * 1.2f ? Mathf.Clamp01(1f - Mathf.Abs(cross) / (b.z * 1.3f + along * 0.25f)) * Mathf.Clamp01(1f - along / (b.z * 9f)) : 0f;
                        white = Mathf.Max(white, wake);
                    }
                    if (s > riverLen - 20f) { white *= (riverLen - s) / 20f; speed *= Mathf.Lerp(0.3f, 1f, (riverLen - s) / 20f); }
                    v.Add(origin + new Vector3(q.x, sy, q.y));
                    c.Add(new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + depth / 24f) * 255f), 0,
                                      (byte)Mathf.RoundToInt(Mathf.Clamp01(white) * 255f), 255));
                    uv.Add(new Vector2((q.x + origin.x) / 26f, (q.y + origin.z) / 26f));
                    uv2.Add(t * speed);
                }
            }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < across; i++)
                {
                    int a = j * (across + 1) + i;
                    tris.Add(a); tris.Add(a + across + 1); tris.Add(a + across + 2);
                    tris.Add(a); tris.Add(a + across + 2); tris.Add(a + 1);
                }
            return WaterObject(parent, "TestRiverWater", v, c, uv, uv2, tris, mat);
        }

        static Renderer WaterObject(Transform parent, string name, List<Vector3> v, List<Color32> c, List<Vector2> uv, List<Vector2> uv2, List<int> tris, Material mat)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.SetTriangles(tris, 0);
            var n = new Vector3[v.Count];
            for (int k = 0; k < n.Length; k++) n[k] = Vector3.up;
            mesh.normals = n;
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }
    }
}
