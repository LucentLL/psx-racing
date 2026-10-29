using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// NIGHT-LOOK PASS 5 IN ONE JOB (2026-10-28). The owner: "The biggest
    /// weakness of the game right now is the city building textures
    /// (especially at night)", with NFS 2015 night and dusk frames and his own
    /// Tryon Sprint frames (dusk/night, the first 25 s from the South End
    /// start: towers as solid yellow grids, a black street with no lamp pools).
    ///   audit - the first 2.5 km of the Tryon Sprint: route metres more than
    ///           40 m from any street light, and every lamp station on its
    ///           edges and why it stood none - with CityMeshes.RouteLamps off
    ///           (main) and on (this pass).
    ///   shots - tryonA / tryonB (the route's start +120 m / +450 m, a chase
    ///           eye looking up the route), i277s10 and high45 (CityFarShots'
    ///           eyes) at night and dusk; noon at tryonA and i277s10 (the day
    ///           must not move).
    ///   haze  - LookChoices.DistantHaze off/on (PSX_DISTHAZE) at tryonB,
    ///           i277s10 and high45 at night, high45 at dusk.
    ///   crown - CrownFrameShots (the open-frame tower's crown; PSX_CROWN_VIEWS,
    ///           default sw,swfar), tagged nl5_TAG, in Screenshots\crown.
    ///   time  - the rooms' cost: tryonB and jb_tryon_eye at night, 30 renders
    ///           each with PSXGlobals.RoomsOn and without.
    ///   eye   - CityPreview's jb_tryon_eye, ht_trade_i77, i277b_2745 at night
    ///           (the night-city pass's own street eyes).
    ///   gles  - VolCloudsCheck: PSX/Lit and friends compiled for WebGL 2.
    /// PSX_NL_SETS (comma list, default all), PSX_NL_TAG (file suffix).
    /// Screenshots\nl5_*_TAG.png and Screenshots\nl5_TAG.txt.
    /// Headless: -executeMethod PSXRacing.EditorTools.NightLookPass.RunBatch
    /// </summary>
    public static class NightLookPass
    {
        const int DispW = 2000, DispH = 1125;
        static string dir, tag;
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void RunBatch()
        {
            int code = 0;
            try { code = Run(); }
            catch (System.Exception e) { Debug.LogException(e); Line("EXCEPTION " + e.Message); code = 1; }
            Flush();
            EditorApplication.Exit(code);
        }

        static void Line(string s) { log.AppendLine(s); Debug.Log("[NightLook5] " + s); }
        static void Flush()
        {
            if (dir == null) return;
            File.WriteAllText(Path.Combine(dir, "nl5_" + (tag ?? "x") + ".txt"), log.ToString());
        }

        static bool Has(HashSet<string> sets, string s) => sets.Count == 0 || sets.Contains(s);

        public static int Run()
        {
            tag = System.Environment.GetEnvironmentVariable("PSX_NL_TAG");
            tag = string.IsNullOrEmpty(tag) ? "x" : tag;
            dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            var sets = new HashSet<string>((System.Environment.GetEnvironmentVariable("PSX_NL_SETS") ?? "").Split(','));
            sets.Remove("");
            Line($"NIGHT-LOOK PASS 5 tag {tag} sets {(sets.Count == 0 ? "all" : string.Join(",", sets))} at {System.DateTime.Now:HH:mm:ss}");

            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Line("Charlotte.unity did not open"); return 1; }
            var map = CityMap.Get();
            if (map == null) { Line("no city data"); return 1; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            if (player != null) player.SetActive(false);
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var keepPixels = PSXQuality.Current;
            var go = new GameObject("~nightLook5World");
            var world = go.AddComponent<CityWorld>();
            string hazeEnv = System.Environment.GetEnvironmentVariable("PSX_DISTHAZE");
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                int lines = PSXQuality.HDLines(DispH);
                int w = CityFarShots.Even(Mathf.RoundToInt(lines * DispW / (float)DispH));
                var route = map.RouteById("tryon");
                if (route == null) { Line("no tryon route"); return 1; }

                if (Has(sets, "audit")) Audit(map, route, world);

                // the eyes
                var spots = new List<(string name, Vector3 eye, Quaternion rot)>();
                if (RoutePose(map, route, 120f, 1.7f, out var pA, out var rA)) spots.Add(("tryonA", pA, rA));
                if (RoutePose(map, route, 450f, 1.7f, out var pB, out var rB)) spots.Add(("tryonB", pB, rB));
                foreach (var f in new[] { ("i277s10", "I-277", -1, 1.0f, 2.2f), ("high45", "I-77", 1, 3.0f, 45f), ("i77s16", "I-77", -1, 1.6f, 2.2f) })
                    if (FarPose(map, f.Item2, f.Item3, f.Item4, f.Item5, out var pf, out var rf)) spots.Add((f.Item1, pf, rf));
                // the rooms up close: the street eyes turned to the facades beside them
                if (spots.Exists(x => x.name == "tryonA")) { var a = spots.Find(x => x.name == "tryonA"); spots.Add(("tryonA_r", a.eye, Quaternion.Euler(-14f, a.rot.eulerAngles.y + 58f, 0f))); }
                if (spots.Exists(x => x.name == "tryonB")) { var b = spots.Find(x => x.name == "tryonB"); spots.Add(("tryonB_r", b.eye, Quaternion.Euler(-16f, b.rot.eulerAngles.y + 34f, 0f))); }
                foreach (var s in spots) Line($"spot {s.name}: eye ({s.eye.x:0.0},{s.eye.y:0.0},{s.eye.z:0.0}) heading {s.rot.eulerAngles.y:0}");

                void Shoot(string spot, int hour, string file)
                {
                    var s = spots.Find(x => x.name == spot);
                    if (s.name == null) { Line("no spot " + spot); return; }
                    world.EnsureRing(s.eye, world.ring);
                    TimeOfDay.Apply(hour, sun);
                    NightGlow.PreviewAll(hour >= TimeOfDay.Dusk);
                    if (globals != null) globals.Apply();
                    cam.transform.SetPositionAndRotation(s.eye, s.rot);
                    world.RefreshSkyline(cam);
                    var rt = new RenderTexture(w, lines, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                    rt.Create();
                    StreetLights.Push(cam.transform.position, cam.transform.forward);
                    PSXTexDecode.StampAll();
                    RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                    var shown = PSXScreenshotTool.Dithered(rt);
                    var tex = CityFarShots.Read(shown);
                    var big = CityFarShots.PointTo(tex, DispW, DispH);
                    File.WriteAllBytes(Path.Combine(dir, $"nl5_{file}_{tag}.png"), big.EncodeToPNG());
                    if (big != tex) Object.DestroyImmediate(big);
                    Object.DestroyImmediate(tex);
                    if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                    rt.Release(); Object.DestroyImmediate(rt);
                    Line($"shot nl5_{file}_{tag}: tiles {world.LiveTiles}, lamps registered {StreetLights.Registered}, field {StreetLights.FieldLamps} (mercury {StreetLights.FieldMercuryLamps}), haze {(globals != null ? globals.distHaze : -1f):0.00}, rooms {(PSXGlobals.RoomsOn ? 1 : 0)}");
                }
                string HourName(int h) => TimeOfDay.All[h].name.ToLowerInvariant();

                if (Has(sets, "shots"))
                {
                    foreach (var sp in new[] { "tryonA", "tryonB", "i277s10", "high45" })
                        foreach (int h in new[] { TimeOfDay.Night, TimeOfDay.Dusk })
                            Shoot(sp, h, $"{sp}_{HourName(h)}");
                    foreach (var sp in new[] { "tryonA", "i277s10" })
                        Shoot(sp, TimeOfDay.Noon, $"{sp}_noon");
                }
                if (Has(sets, "rooms"))
                {
                    bool keepRooms = PSXGlobals.RoomsOn;
                    foreach (var sp in new[] { "tryonA_r", "tryonB_r" })
                    {
                        PSXGlobals.RoomsOn = false;
                        Shoot(sp, TimeOfDay.Night, $"rooms_{sp}_night_A_flat");
                        PSXGlobals.RoomsOn = true;
                        Shoot(sp, TimeOfDay.Night, $"rooms_{sp}_night_B_rooms");
                    }
                    PSXGlobals.RoomsOn = keepRooms;
                }
                if (Has(sets, "noon0"))
                {
                    // the day unchanged: noon with this pass's lamp posts off (main's
                    // streets), to diff against main's own noon frames
                    bool keepL = CityMeshes.RouteLamps;
                    CityMeshes.RouteLamps = false;
                    world.DropAll();
                    foreach (var sp in new[] { "tryonA", "i277s10" }) Shoot(sp, TimeOfDay.Noon, $"{sp}_noon_lampsoff");
                    CityMeshes.RouteLamps = keepL;
                    world.DropAll();
                }
                if (Has(sets, "haze"))
                {
                    foreach (var (sp, h) in new[] { ("tryonB", TimeOfDay.Night), ("i277s10", TimeOfDay.Night), ("i77s16", TimeOfDay.Night), ("high45", TimeOfDay.Night), ("high45", TimeOfDay.Dusk), ("tryonB", TimeOfDay.Dusk) })
                    {
                        System.Environment.SetEnvironmentVariable("PSX_DISTHAZE", "0");
                        Shoot(sp, h, $"haze_{sp}_{HourName(h)}_A_off");
                        System.Environment.SetEnvironmentVariable("PSX_DISTHAZE", "1");
                        Shoot(sp, h, $"haze_{sp}_{HourName(h)}_B_on");
                    }
                    System.Environment.SetEnvironmentVariable("PSX_DISTHAZE", hazeEnv);
                }
                if (Has(sets, "time"))
                {
                    foreach (var sp in new[] { "tryonB_r", "tryonA_r", "tryonB" })
                    {
                        var s = spots.Find(x => x.name == sp);
                        if (s.name == null) continue;
                        world.EnsureRing(s.eye, world.ring);
                        TimeOfDay.Apply(TimeOfDay.Night, sun);
                        NightGlow.PreviewAll(true);
                        cam.transform.SetPositionAndRotation(s.eye, s.rot);
                        world.RefreshSkyline(cam);
                        bool keep = PSXGlobals.RoomsOn;
                        const int reps = 4, n = 40;
                        var ons = new List<float>(); var offs = new List<float>();
                        for (int rep = 0; rep < reps; rep++)
                        {
                            PSXGlobals.RoomsOn = true; if (globals != null) globals.Apply();
                            ons.Add(CityFarShots.TimeRenders(cam, w, lines, n));
                            PSXGlobals.RoomsOn = false; if (globals != null) globals.Apply();
                            offs.Add(CityFarShots.TimeRenders(cam, w, lines, n));
                        }
                        PSXGlobals.RoomsOn = keep; if (globals != null) globals.Apply();
                        ons.Sort(); offs.Sort();
                        Line($"TIME {sp} night {w}x{lines}, {reps}x{n} renders each: rooms on min {ons[0]:0.00} median {ons[reps / 2]:0.00} ms, off min {offs[0]:0.00} median {offs[reps / 2]:0.00} ms (min +{ons[0] - offs[0]:0.00})");
                    }
                }
                world.DropAll();
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_DISTHAZE", hazeEnv);
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }

            if (Has(sets, "eye"))
            {
                System.Environment.SetEnvironmentVariable("PSX_EYE_VIEWS", "jb_tryon_eye,ht_trade_i77,i277b_2745");
                System.Environment.SetEnvironmentVariable("PSX_EYE_HOURS", "night");
                CityPreview.RunEyePlay();
                foreach (var f in Directory.GetFiles(dir, "eye_*_night.png"))
                {
                    string to = Path.Combine(dir, "nl5_" + Path.GetFileNameWithoutExtension(f) + "_" + tag + ".png");
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(f, to);
                    Line("eye " + Path.GetFileName(to));
                }
            }
            if (Has(sets, "crown"))
            {
                // the open-frame tower's crown, by CrownFrameShots in its own world
                string cv = System.Environment.GetEnvironmentVariable("PSX_CROWN_VIEWS");
                string ct = System.Environment.GetEnvironmentVariable("PSX_CROWN_TAG");
                if (string.IsNullOrEmpty(cv)) System.Environment.SetEnvironmentVariable("PSX_CROWN_VIEWS", "sw,swfar");
                System.Environment.SetEnvironmentVariable("PSX_CROWN_TAG", "nl5_" + tag);
                CrownFrameShots.Run();
                System.Environment.SetEnvironmentVariable("PSX_CROWN_VIEWS", cv);
                System.Environment.SetEnvironmentVariable("PSX_CROWN_TAG", ct);
                Line("crown shots: Screenshots/crown/crown_*_nl5_" + tag + ".png");
            }
            int bad = 0;
            if (Has(sets, "gles"))
            {
                bad = VolCloudsCheck.Run();
                Line("GLES3 compile (VolCloudsCheck: PSX/Lit, PSX/CarPaint, the skies): " + (bad == 0 ? "OK" : bad + " error(s)"));
            }
            Line("DONE " + System.DateTime.Now.ToString("HH:mm:ss"));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  the route
        // ------------------------------------------------------------------

        struct RS { public Vector2 p, t; public int edge; public float s, y; }

        /// <summary>The route's centreline from fromM past its start line,
        /// every step metres for lenM metres.</summary>
        static List<RS> RouteSamples(CityMap map, CityMap.Route r, float fromM, float lenM, float step)
        {
            var list = new List<RS>();
            float acc = 0f, want = r.startM + fromM, end = r.startM + fromM + lenM;
            for (int k = 0; k < r.edges.Length && k < r.dirs.Length && want <= end; k++)
            {
                var e = map.edges[r.edges[k]];
                bool fwd = r.dirs[k] > 0;
                while (want <= acc + e.length && want <= end)
                {
                    float into = Mathf.Clamp(want - acc, 0f, e.length);
                    float s = fwd ? into : e.length - into;
                    list.Add(new RS { p = e.PointAt(s), t = e.TangentAt(s) * (fwd ? 1f : -1f), edge = e.index, s = s, y = e.YAt(s) });
                    want += step;
                }
                acc += e.length;
            }
            return list;
        }

        /// <summary>A chase eye on the route: in the right-hand lane of a
        /// two-way street, looking up the route and a little up.</summary>
        static bool RoutePose(CityMap map, CityMap.Route r, float atM, float up, out Vector3 eye, out Quaternion rot)
        {
            eye = default; rot = Quaternion.identity;
            var a = RouteSamples(map, r, atM, 40f, 40f);
            if (a.Count < 2) return false;
            var e = map.edges[a[0].edge];
            var right = new Vector2(a[0].t.y, -a[0].t.x).normalized;
            float lateral = e.oneway ? 0f : e.width * 0.25f;
            var p = a[0].p + right * lateral;
            eye = new Vector3(p.x, a[0].y + up, p.y);
            var ahead = a[1].p + right * lateral;
            var look = new Vector3(ahead.x, a[1].y + up + 40f * 0.07f, ahead.y);
            rot = Quaternion.LookRotation(look - eye);
            return true;
        }

        /// <summary>CityFarShots' eye: on the named road, km from uptown on
        /// that side, up metres over it, looking at uptown 40 m up.</summary>
        static bool FarPose(CityMap map, string road, int side, float km, float up, out Vector3 eye, out Quaternion rot)
        {
            eye = default; rot = Quaternion.identity;
            float best = float.MaxValue; int bei = -1; float bs = 0f;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.link || e.name != road) continue;
                for (float s = 0f; s <= e.length; s += 10f)
                {
                    var p = e.PointAt(s);
                    if (Mathf.Sign(p.y - map.uptown.y) != side) continue;
                    float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - km * 1000f);
                    if (d < best) { best = d; bei = ei; bs = s; }
                }
            }
            if (bei < 0) return false;
            var be = map.edges[bei];
            var at = be.PointAt(bs);
            eye = new Vector3(at.x, be.YAt(bs) + up, at.y);
            var core = new Vector3(map.uptown.x, eye.y + 40f, map.uptown.y);
            rot = Quaternion.LookRotation(core - eye);
            return true;
        }

        // ------------------------------------------------------------------
        //  the Tryon lamp audit
        // ------------------------------------------------------------------

        const float AuditFromM = 0f, AuditLenM = 2500f, DarkM = 40f;

        static void Audit(CityMap map, CityMap.Route route, CityWorld world)
        {
            var samples = RouteSamples(map, route, AuditFromM, AuditLenM, 5f);
            var routeEdges = new HashSet<int>();
            foreach (var s in samples) routeEdges.Add(s.edge);
            int poleEdges = 0, acornEdges = 0, otherEdges = 0;
            foreach (int ei in routeEdges)
            {
                var e = map.edges[ei];
                if (CityPoles.IsPoleEdge(map, e)) poleEdges++;
                else if (CityPoles.IsAcornEdge(map, e)) acornEdges++;
                else otherEdges++;
            }
            Line($"AUDIT tryon route: {samples.Count * 5f / 1000f:0.00} km from its start line ({route.startM:0} m), {routeEdges.Count} edges: {poleEdges} pole roads, {acornEdges} acorn (uptown), {otherEdges} other");
            bool keep = CityMeshes.RouteLamps;
            foreach (bool on in new[] { false, true })
            {
                CityMeshes.RouteLamps = on;
                world.DropAll();
                var why = new int[CityMeshes.LampRejectCount];
                int stations = 0, placed = 0;
                CityMeshes.LampTrace = (edge, s, w) =>
                {
                    if (!routeEdges.Contains(edge) || s < 0f) return;
                    stations++;
                    if (w == 0) placed++;
                    else if (w > 0 && w < why.Length) why[w]++;
                };
                int dark = 0, n = 0, poleSamples = 0;
                float darkRun = 0f, worstRun = 0f; string worstAt = "";
                int lampsNear = 0;
                for (int i = 0; i < samples.Count; i += 40)
                {
                    // a 3x3 ring round every 200 m of route (tiles 256 m): every
                    // sample measured lies 100 m or less from the ring's centre
                    var c = samples[Mathf.Min(i + 20, samples.Count - 1)];
                    world.EnsureRing(new Vector3(c.p.x, c.y, c.p.y), 1);
                    for (int j = i; j < Mathf.Min(i + 40, samples.Count); j++)
                    {
                        var q = samples[j];
                        int near = StreetLights.CountWithin(new Vector3(q.p.x, q.y + 8f, q.p.y), DarkM, StreetLights.Kind.Street);
                        n++; lampsNear += near;
                        if (CityPoles.IsPoleEdge(map, map.edges[q.edge])) poleSamples++;
                        if (near == 0) { dark++; darkRun += 5f; if (darkRun > worstRun) { worstRun = darkRun; worstAt = $"({q.p.x:0},{q.p.y:0}) on '{map.edges[q.edge].name}'"; } }
                        else darkRun = 0f;
                    }
                }
                CityMeshes.LampTrace = null;
                var row = new List<string>();
                for (int k = 1; k < why.Length; k++) if (why[k] > 0) row.Add($"{CityMeshes.LampRejectNames[k]} {why[k]}");
                Line($"AUDIT RouteLamps={(on ? "ON (this pass)" : "OFF (main)")}: {dark * 5f:0} m of {n * 5f:0} m ({100f * dark / Mathf.Max(1, n):0.0}%) more than {DarkM:0} m from any street light; " +
                     $"mean lamps within {DarkM:0} m {lampsNear / (float)Mathf.Max(1, n):0.00}; longest dark run {worstRun:0} m at {worstAt}; " +
                     $"lamp stations on the route's edges {stations}, placed {placed}; refused: {(row.Count > 0 ? string.Join(", ", row) : "none")}; route samples on pole roads {poleSamples}");
            }
            CityMeshes.RouteLamps = keep;
            world.DropAll();
        }
    }
}
