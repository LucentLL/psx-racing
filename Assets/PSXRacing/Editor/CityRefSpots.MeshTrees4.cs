using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S OWN TREES, ROUND 4, PROVED (trees4, 2026-11-15). ONE MODE A
    /// PROCESS: what PSX_CT_CHARLOTTE says when the editor starts (0 = round
    /// 3's trees, the BASE; unset = the owner's models by site, AFTER) - so a
    /// before and an after come from two fresh processes on one kit. At round
    /// 3's places (CityRefSpots.T3Places, same rings, same poses):
    ///   * THE SURVEY: every drawn tree's height and crown width by site, the
    ///     species planted there (model shares), Charlotte forms v old forms;
    ///   * the city foliage audit (trees through solids, trunks v drawn trees);
    ///   * the driver's-eye frame (noon, summer) at every place that poses;
    ///   * the four seasons (spring, summer, fall, winter) at two poses
    ///     (subdivision, arterial), a night frame (West Trade), and in the
    ///     AFTER run the close frames (a young willow oak, a young loblolly and
    ///     a small NatureManufacture beech staged 3 m and 8 m from the eye) and
    ///     an impostor pair (the meshes v every tree as its distance card);
    ///   * the cost: ring triangles per FOREST DETAIL, the fit's ms a tile,
    ///     the tree pool's GPU bytes, a timed frame at three places.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunMeshTrees4
    ///   PSX_CT4_TAG=base|after  PSX_CT_REKIT=1  PSX_CT3_ONLY=place,place  PSX_CT3_NOSHOTS=1
    /// Output: Screenshots/ctrees4/&lt;tag&gt;/*.png, ctrees4_&lt;tag&gt;_log.txt, ctrees4_&lt;tag&gt;_survey.csv
    /// </summary>
    public static partial class CityRefSpots
    {
        static readonly string[] T4SeasonPlaces = { "subdivision", "arterial" };

        public static void RunMeshTrees4()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_CT4_TAG");
            if (string.IsNullOrEmpty(tag)) tag = CityMeshTrees.Charlotte ? "after" : "base";
            var log = new StringBuilder($"CITY MESH TREES ROUND 4 ({tag}: PSX_CT_CHARLOTTE {(CityMeshTrees.Charlotte ? "on" : "OFF")}) " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctrees4");
            string dir = Path.Combine(root, tag);
            Directory.CreateDirectory(dir);
            var csv = new StringBuilder("mode,place,site,species,street,model,height_m,crown_w_m,scale\n");
            int code = 0, shots = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var staged = new List<GameObject>();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kitAsset = PSXRacingBuilder.EnsureCityTreeKit(System.Environment.GetEnvironmentVariable("PSX_CT_REKIT") == "1");
                if (kitAsset == null) throw new System.Exception("no city tree kit (store files?)");
                log.Append($"kit ready at {clock.Elapsed.TotalSeconds:0} s; distance cards this build: {PSXRacingBuilder.CT4SheetCount} sheets ({PSXRacingBuilder.CT4SheetsWritten} written), {PSXRacingBuilder.CT4SheetGpuBytes / 1048576.0:0.0} MB\n");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var dressing = Object.FindAnyObjectByType<SeasonDress>();
                CityMeshTrees.ResetCounts();
                CityMeshTrees.Round1Mode = false;
                CityMeshTrees.RecordSites = true;
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                if (!mt.Round3Ready) throw new System.Exception("the kit has no round-3 forms");
                if (CityMeshTrees.Charlotte && !mt.CharlotteReady) throw new System.Exception("PSX_CT_CHARLOTTE on but the kit has no Charlotte forms (tools/charlotte-trees.ps1, PSX_CT_REKIT=1)");
                int n4 = 0;
                for (int v = 0; v < kit.variants.Length; v++) if (kit.Group3(v) == CityTreeKit.G4Charlotte) n4++;
                log.Append($"kit: {kit.variants.Length} species ({n4} Charlotte forms); LOD {mt.Forest.lod0To:0}/{mt.Forest.lod1To:0}/{mt.Forest.meshRange:0} m, impostors to {mt.Forest.farRange:0} m (FOREST DETAIL {ForestDetailPrefs.Label}); pool {mt.Forest.PoolSlots} slots; GPU buffers {T4GpuBytes(mt.Forest) / 1048576.0:0.0} MB\n");
                for (int v = 0; v < kit.variants.Length; v++)
                    if (kit.Group3(v) == CityTreeKit.G4Charlotte)
                        log.Append($"  {v} {kit.names[v]} {kit.variants[v].height:0.0} m crown from {kit.crownBase[v]:0.00}, widest r {MaxR(kit, v):0.0} m, tris {LT(kit, v, 0)}/{LT(kit, v, 1)}/{LT(kit, v, 2)}\n");

                string only = System.Environment.GetEnvironmentVariable("PSX_CT3_ONLY");
                bool noShots = System.Environment.GetEnvironmentVariable("PSX_CT3_NOSHOTS") == "1";
                var audit = new AuditSum();
                var acc = new Dictionary<int, T3Acc>();
                var tot = new double[12];
                int tilesSurveyed = 0, charlotte = 0, oldForm = 0;
                var seenTiles = new HashSet<long>();

                foreach (var pl in T3Places)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.wooded) { if (!T3Wooded(map, LL(pl.lat, pl.lon), 3000f, out e, out s)) { log.Append($"{pl.id}: no wooded road\n"); continue; } }
                    else if (pl.localOnly) { if (!T3SnapLocal(map, LL(pl.lat, pl.lon), out e, out s)) { log.Append($"{pl.id}: no residential street near\n"); continue; } }
                    else if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append($"{pl.id}: no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    CityMeshTrees.ResetCounts();
                    var tc = System.Diagnostics.Stopwatch.StartNew();
                    world.EnsureRing(anchor, pl.ring); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    bool posed = CtPose(cp, map, mt, e, s, out var eye, out var fwd, out var farEye, out string how);
                    if (!posed) { eye = anchor + Vector3.up * 1.15f; fwd = Vector3.forward; how = "survey only (" + how + ")"; }
                    var rot = Quaternion.LookRotation(fwd + Vector3.down * 0.03f);
                    log.Append($"\n{pl.id}\t'{e.name}' cls {e.cls}{(e.oneway ? " one-way" : "")} w {e.width:0.0}{(named ? "" : " (NOT a named road of the place)")}\t{CityAudit.LatLon(eye.x, eye.z)} eye ({eye.x:0.0}, {eye.y:0.0}, {eye.z:0.0}) fwd ({fwd.x:0.00}, {fwd.z:0.00}); {how}\t{pl.what}\n");
                    if ((new Vector2(eye.x, eye.z) - p0).magnitude > 120f) { world.EnsureRing(eye, pl.ring); mt.Flush(); }
                    double ringMs = tc.Elapsed.TotalMilliseconds;
                    int all = CityMeshTrees.Placed + CityMeshTrees.Dropped;
                    log.Append($"  {tag}: {CityMeshTrees.Fitted} tiles, {all} trees, placed {CityMeshTrees.Placed} (Charlotte forms {CityMeshTrees.PlacedCharlotte}, old forms {CityMeshTrees.PlacedOldForm}), dropped {CityMeshTrees.Dropped} ({Pct(CityMeshTrees.Dropped, all)}), moved {CityMeshTrees.Moved}; street {CityMeshTrees.StreetIn} dropped {CityMeshTrees.StreetDropped}; made breakaway {CityMeshTrees.Softened}; " +
                               $"fit {CityMeshTrees.FitMs:0} ms ({CityMeshTrees.FitMs / Mathf.Max(1, CityMeshTrees.Fitted):0.0} a tile, worst tile {CityMeshTrees.FitMsMaxTile:0.0}, worst tree {CityMeshTrees.TreeMsMax:0.00} ms), tests max {CityMeshTrees.TestsMax}, capped {CityMeshTrees.CappedTrees}; ring built in {ringMs / 1000.0:0.0} s\n");
                    tot[0] += all; tot[1] += CityMeshTrees.Dropped; tot[2] += CityMeshTrees.StreetIn; tot[3] += CityMeshTrees.StreetDropped;
                    tot[4] += CityMeshTrees.FitMs; tot[5] += CityMeshTrees.Fitted; tot[6] = System.Math.Max(tot[6], CityMeshTrees.FitMsMaxTile);
                    tot[7] = System.Math.Max(tot[7], CityMeshTrees.TreeMsMax); tot[9] += CityMeshTrees.Softened;
                    charlotte += CityMeshTrees.PlacedCharlotte; oldForm += CityMeshTrees.PlacedOldForm;
                    foreach (long key in mt.FittedKeys)
                    {
                        var plants = mt.PlantsOf(key); var fr = mt.FrameOf(key); var cx = mt.CtxOf(key);
                        if (plants == null || fr == null || cx == null) continue;
                        if (seenTiles.Add(key)) tilesSurveyed++;
                        for (int i = 0; i < plants.Count && i < cx.Count; i++)
                        {
                            var pp = plants[i];
                            float hh = kit.variants[pp.variant].height * pp.scale, ww = 2f * MaxR(kit, pp.variant) * pp.scale;
                            if (!acc.TryGetValue(cx[i], out var a)) acc[cx[i]] = a = new T3Acc();
                            a.h.Add(hh); a.w.Add(ww);
                            string mn = kit.names[pp.variant];
                            a.models.TryGetValue(mn, out int mc); a.models[mn] = mc + 1;
                            a.tris0 += LT(kit, pp.variant, 0);
                            var t = i < fr.trees.Count ? fr.trees[i] : default;
                            csv.Append(tag).Append(',').Append(pl.id).Append(',').Append(CityMeshTrees.CtxNames[cx[i]]).Append(',').Append(t.species).Append(',').Append(t.street ? 1 : 0).Append(',')
                               .Append(mn).Append(',').Append(hh.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                               .Append(ww.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(pp.scale.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                        }
                    }
                    AuditRing(mt, map, world, audit, log);
                    if (!posed || noShots) continue;
                    if (car != null) car.TeleportTo(eye - fwd * 3f - Vector3.up * 0.8f, Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z)));
                    // ---- noon, summer: every place ----
                    T4Shoot(cam, sun, dressing, world, mt, dir, pl.id + "_noon_summer", eye, rot, TimeOfDay.Noon, (int)Season.Summer);
                    T4Labels(cam, mt, kit, Path.Combine(dir, pl.id + "_noon_summer_labels.csv"), eye, rot);
                    shots++;
                    // ---- the four seasons at two poses ----
                    if (System.Array.IndexOf(T4SeasonPlaces, pl.id) >= 0)
                        foreach (var (sn, d) in new[] { ("spring", (int)Season.Spring), ("fall", (int)Season.Fall), ("winter", (int)Season.Winter) })
                        { T4Shoot(cam, sun, dressing, world, mt, dir, pl.id + "_noon_" + sn, eye, rot, TimeOfDay.Noon, d); shots++; }
                    // ---- a night frame ----
                    if (pl.id == "wtrade") { T4Shoot(cam, sun, dressing, world, mt, dir, pl.id + "_night_summer", eye, rot, TimeOfDay.Night, (int)Season.Summer); shots++; }
                    // ---- the cost at three places ----
                    if (pl.id == "uptown" || pl.id == "subdivision" || pl.id == "woodland") { T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer); Cost(cam, mt, pl.id + " " + tag, eye, rot, log); }
                    else T4RingTris(mt, pl.id + " " + tag, eye, log);
                    // ---- AFTER: the impostor pair, the close frames ----
                    if (CityMeshTrees.Charlotte && pl.id == "arterial")
                    {
                        T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer);
                        var keep = CityMeshTrees.LodOverride;
                        var b = keep.x > 0f ? keep : new Vector3(kit.lod0To, kit.lod1To, kit.meshRange);
                        for (int k = 0; k < 2; k++)
                        {
                            CityMeshTrees.LodOverride = k == 0 ? new Vector3(b.x, b.y, 600f) : new Vector3(0.4f, 0.6f, 1f);
                            mt.Rebuild();
                            cam.transform.SetPositionAndRotation(eye, rot);
                            SunShadows.RenderFor(cam);
                            MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctrees4/{tag}/{pl.id}_impostor_{(k == 0 ? "meshes" : "cards")}", eye, rot);
                            shots++;
                        }
                        CityMeshTrees.LodOverride = keep; mt.Rebuild();
                        shots += T4Close(cam, sun, dressing, world, mt, map, kit, tag, eye, staged, log);
                    }
                }
                world.DropAll(); mt.Flush();

                // ---------------- THE SURVEY'S TABLES ----------------
                log.Append($"\nSURVEY {tag.ToUpperInvariant()}: height and crown width (widest reach x 2) of every drawn tree, by site\n");
                for (int c = 0; c < CityMeshTrees.CtxNames.Length; c++)
                {
                    if (!acc.TryGetValue(c, out var a) || a.h.Count == 0) continue;
                    var hs = new List<float>(a.h); hs.Sort();
                    var ws = new List<float>(a.w); ws.Sort();
                    float Q(List<float> l, float q) => l[Mathf.Min(l.Count - 1, (int)(q * l.Count))];
                    double mh = 0, mw = 0; for (int i = 0; i < a.h.Count; i++) { mh += a.h[i]; mw += a.w[i]; }
                    mh /= a.h.Count; mw /= a.w.Count;
                    var bins = new int[T3Bins.Length - 1];
                    foreach (float x in a.h) for (int bb = 0; bb < bins.Length; bb++) if (x >= T3Bins[bb] && x < T3Bins[bb + 1]) { bins[bb]++; break; }
                    var sbb = new StringBuilder();
                    for (int bb = 0; bb < bins.Length; bb++) sbb.Append($" {T3Bins[bb]:0}-{(T3Bins[bb + 1] > 90f ? "+" : T3Bins[bb + 1].ToString("0"))}:{100f * bins[bb] / a.h.Count:0}%");
                    var ml = new List<KeyValuePair<string, int>>(a.models); ml.Sort((x, y) => y.Value.CompareTo(x.Value));
                    var sbm = new StringBuilder();
                    int nOld = 0;
                    foreach (var kv in ml) if (!kv.Key.StartsWith("CT4_")) nOld += kv.Value;
                    for (int i = 0; i < Mathf.Min(12, ml.Count); i++) sbm.Append($" {ml[i].Key.Replace("CT4_", "")} {100f * ml[i].Value / a.h.Count:0.0}%;");
                    log.Append($"  {CityMeshTrees.CtxNames[c],-16} n {a.h.Count,6}  height mean {mh:0.0} p10 {Q(hs, 0.1f):0.0} p50 {Q(hs, 0.5f):0.0} p90 {Q(hs, 0.9f):0.0} max {hs[hs.Count - 1]:0.0} m | crown width mean {mw:0.0} p50 {Q(ws, 0.5f):0.0} m | width/height {mw / mh:0.00} | LOD0 tris a tree {a.tris0 / a.h.Count:0} | old (round 1-3) models {100f * nOld / a.h.Count:0.0}%\n" +
                               $"      heights (m):{sbb}\n      models ({ml.Count}):{sbm}\n");
                }
                log.Append($"  TOTAL {tag}: {tot[0]:0} trees, dropped {tot[1]:0} ({Pct((int)tot[1], (int)tot[0])}); street {tot[2]:0} dropped {tot[3]:0}; made breakaway {tot[9]:0}; Charlotte forms {charlotte}, old forms where none fitted {oldForm}; " +
                           $"fit {tot[4]:0} ms over {tot[5]:0} tiles ({tot[4] / System.Math.Max(1, tot[5]):0.0} ms a tile), worst tile {tot[6]:0.0} ms, worst tree {tot[7]:0.00} ms\n");
                log.Append($"  tiles surveyed: {tilesSurveyed}\n");
                log.Append($"\nCITY FOLIAGE AUDIT {tag.ToUpperInvariant()}: {audit.tiles} tiles, {audit.trees} mesh trees, {audit.points} crown points tested -> trees through solids {audit.bad} (must be 0): road envelope {audit.road}, building {audit.bld}, pole/wire {audit.pole}, signal {audit.sig}, collider {audit.col}; trunks standing with no drawn tree {audit.colliderOnly} (must be 0)\n");
                foreach (var w in audit.worst) log.Append("  " + w + "\n");
                log.Append($"\npool growths {CityMeshTrees.PoolGrowths}; GPU buffers {T4GpuBytes(mt.Forest) / 1048576.0:0.0} MB; {shots} frames to {dir}; {clock.Elapsed.TotalSeconds:0} s\nCITY MESH TREES 4 OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY MESH TREES 4 FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
                CityMeshTrees.RecordSites = false;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(root, $"ctrees4_{tag}_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(root, $"ctrees4_{tag}_survey.csv"), csv.ToString());
                Debug.Log("[CityMeshTrees4]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The tree pool's GPU buffers (MeshForest's own count, read without changing MeshForest).</summary>
        static long T4GpuBytes(MeshForest f)
        {
            var fi = typeof(MeshForest).GetField("gpuBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f != null && fi != null ? (long)fi.GetValue(f) : -1;
        }

        static void T4Light(Light sun, SeasonDress dressing, CityWorld world, int hour, int dress)
        {
            RaceHandoff.CalendarDay = 0;
            TimeOfDay.Apply(hour, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            bool lights = TimeOfDay.At(hour).lightsOn;
            NightGlow.PreviewAll(lights);
            foreach (var cl in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) cl.PreviewBuild(lights);
            SetDress(dressing, world, dress);
            CarLights.PushGlobals();
        }

        static void T4Shoot(Camera cam, Light sun, SeasonDress dressing, CityWorld world, CityMeshTrees mt, string dir, string name, Vector3 eye, Quaternion rot, int hour, int dress)
        {
            T4Light(sun, dressing, world, hour, dress);
            cam.transform.SetPositionAndRotation(eye, rot);
            SunShadows.RenderFor(cam);
            MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
            PSXScreenshotTool.ShotAs(cam, "ctrees4/" + Path.GetFileName(dir) + "/" + name, eye, rot);
        }

        /// <summary>The ring's mesh-tree triangles from the eye in every direction, per FOREST DETAIL (Cost's second half).</summary>
        static void T4RingTris(CityMeshTrees mt, string place, Vector3 eye, StringBuilder log)
        {
            var kit = mt.Kit;
            float[] sc = { 0.65f, 1f, 1.5f };
            Vector3[] mul = { new Vector3(2.15f, 1.2f, 1.3f), new Vector3(1.85f, 1f, 1f), new Vector3(1.5f, 1f, 1f) };
            var sb = new StringBuilder($"  RING TRIS {place} ({mt.LivePlants()} plants in {mt.LiveTiles} tiles):");
            var b0 = CityMeshTrees.LodOverride.x > 0f ? CityMeshTrees.LodOverride : new Vector3(kit.lod0To, kit.lod1To, kit.meshRange);
            for (int q = 0; q < 3; q++)
            {
                float l0 = b0.x * sc[q] * mul[q].x, l1 = b0.y * sc[q] * mul[q].y, mr = b0.z * sc[q] * mul[q].z;
                double tris = 0;
                foreach (long key in mt.FittedKeys)
                {
                    var plants = mt.PlantsOf(key); if (plants == null) continue;
                    foreach (var p in plants)
                    {
                        float d = Vector3.Distance(p.pos, eye);
                        int lv = d < l0 ? 0 : d < l1 ? 1 : d < mr ? 2 : 3;
                        tris += lv < 3 ? LT(kit, p.variant, lv) : 4;
                    }
                }
                sb.Append($" {new[] { "LOW", "MEDIUM", "HIGH" }[q]} {tris / 1e6:0.00} M;");
            }
            log.Append(sb).Append('\n');
        }

        /// <summary>The models close up, the owner's models beside a NatureManufacture tree: a young
        /// willow oak, a young loblolly and a small Forest beech STAGED ON A LAWN (owner 11-15, on the
        /// first staging in the lane: "that is a tree in the center of the road...this should never
        /// happen"): every staged trunk where a tree really stands (T4Lawn: off the road and its walk,
        /// clear of buildings, lots and planted trees), the camera on the lawn or the walk looking at
        /// them across the grass, away from the road; the nearest trunk 3 m and 8 m from the eye,
        /// noon, summer. A comparison of the models, not planting: the frames are labelled so.</summary>
        static int T4Close(Camera cam, Light sun, SeasonDress dressing, CityWorld world, CityMeshTrees mt, CityMap map, CityTreeKit kit, string tag,
                           Vector3 near, List<GameObject> staged, StringBuilder log)
        {
            int Find(string n) => kit.names != null ? System.Array.IndexOf(kit.names, n) : -1;
            int oak = Find("CT4_WillowOak_YoungStreet"), pine = Find("CT4_LoblollyPine_Young"), beech = -1;
            for (int v = 0; v < kit.variants.Length && beech < 0; v++) if (kit.Group3(v) == 0 && kit.roles[v] == CityTreeKit.Role.Small) beech = v;
            if (oak < 0 || pine < 0 || beech < 0) { log.Append($"  CLOSE: a model missing (oak {oak}, pine {pine}, beech {beech})\n"); return 0; }
            Physics.SyncTransforms();
            if (!T4Lawn(map, mt, kit, near, out var c, out var f, out string how)) { log.Append("  CLOSE: no lawn found near the place (" + how + "): no close frames\n"); return 0; }
            T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer);
            var right = Vector3.Cross(Vector3.up, f);
            int n = 0;
            foreach (float D in new[] { 3f, 8f })
            {
                foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
                staged.Clear();
                float gap = D < 5f ? 2.6f : 5.5f;
                int[] which = { oak, pine, beech };
                for (int k = 0; k < 3; k++)
                {
                    var v = kit.variants[which[k]];
                    var go = new GameObject("T4Close_" + kit.names[which[k]]) { hideFlags = HideFlags.DontSave };
                    var p2 = c + right * ((k - 1) * gap);
                    var pos = new Vector3(p2.x, T4Ground(p2, near.y) - CityTrees.SinkM, p2.z);
                    // the variant's own frame (pre) under the plant's
                    var m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, 37f * k, 0f), Vector3.one) * v.pre;
                    go.transform.position = m.GetColumn(3);
                    go.transform.rotation = m.rotation;
                    go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = v.lods[0];
                    var mats = new Material[v.lods[0].subMeshCount];
                    for (int s = 0; s < mats.Length; s++)
                    {
                        bool leaf = v.leafSub != null && s < v.leafSub.Length && v.leafSub[s];
                        mats[s] = leaf && v.leafDress != null ? v.leafDress[(int)Season.Summer] : v.mats[Mathf.Min(s, v.mats.Length - 1)];
                    }
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    staged.Add(go);
                }
                var cam2 = c - f * D;
                var camPos = new Vector3(cam2.x, T4Ground(cam2, near.y) + 1.6f, cam2.z);
                var r = Quaternion.LookRotation(f + Vector3.up * (D < 5f ? 0.32f : 0.22f));
                cam.transform.SetPositionAndRotation(camPos, r);
                SunShadows.RenderFor(cam);
                MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                PSXScreenshotTool.ShotAs(cam, $"ctrees4/{tag}/close_{D:0}m", camPos, r);
                n++;
            }
            foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
            staged.Clear();
            log.Append($"  CLOSE (STAGED COMPARISON on a lawn at {CityAudit.LatLon(c.x, c.z)}; {how}): {kit.names[oak]} (left), {kit.names[pine]} (middle), {kit.names[beech]} (right, NatureManufacture Forest), the nearest trunk 3 m and 8 m from the eye\n");
            return n;
        }

        static float T4Ground(Vector3 p, float nearY)
        {
            var from = new Vector3(p.x, nearY + 60f, p.z);
            var hits = Physics.RaycastAll(from, Vector3.down, 140f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - nearY) < 12f && (float.IsNaN(best) || h.point.y < best)) best = h.point.y;
            return float.IsNaN(best) ? nearY : best;
        }

        static float T4SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            return Vector2.Distance(p, a + d * t);
        }

        static bool T4Inside(Vector2[] ring, Vector2 p)
        {
            if (ring == null || ring.Length < 3) return false;
            bool inside = false;
            for (int i = ring.Length - 1, j = 0; j < ring.Length; i = j++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) inside = !inside;
            return inside;
        }

        /// <summary>A LAWN for the staged comparison, the nearest to <paramref name="near"/>: the row of
        /// three trunks (both spacings) at least 5.5 m from every road's pavement edge (a 0.6 m gutter and
        /// curb, a walk to 3.9 m, then 1 m), 7 m from every building, outside every parking lot, 6 m from
        /// every planted tree, on ground within a metre of level; the camera spots (3 m and 8 m in front)
        /// 1.5 m off every pavement, outside buildings and lots, 2 m from planted trees, looking away from
        /// the nearest road.</summary>
        static bool T4Lawn(CityMap map, CityMeshTrees mt, CityTreeKit kit, Vector3 near, out Vector3 centre, out Vector3 dir, out string how)
        {
            centre = near; dir = Vector3.forward; how = "";
            var n2 = new Vector2(near.x, near.z);
            const float Reach = 320f;
            var segA = new List<Vector2>(); var segB = new List<Vector2>(); var segH = new List<float>();
            foreach (var e in map.edges)
            {
                if (e.tunnel || e.length < 1f) continue;
                if (Vector2.Distance(e.PointAt(e.length * 0.5f), n2) > Reach + e.length * 0.5f) continue;
                int k = Mathf.Max(1, Mathf.CeilToInt(e.length / 4f));
                var prev = e.PointAt(0f);
                for (int i = 1; i <= k; i++) { var q = e.PointAt(e.length * i / k); segA.Add(prev); segB.Add(q); segH.Add(e.width * 0.5f); prev = q; }
            }
            var fps = new List<CityMap.Footprint>(); var fpR = new List<float>();
            if (map.footprints != null)
                foreach (var fp in map.footprints)
                {
                    if (fp == null || fp.replaced || fp.pts == null || fp.pts.Length < 3) continue;
                    if (Vector2.Distance(fp.centre, n2) > Reach + 60f) continue;
                    float r = 0f; foreach (var q in fp.pts) r = Mathf.Max(r, (q - fp.centre).magnitude);
                    fps.Add(fp); fpR.Add(r);
                }
            var lots = new List<Vector2[]>();
            if (map.lots != null)
                foreach (var lot in map.lots)
                    if (lot != null && lot.ring != null && lot.ring.Length >= 3 && Vector2.Distance(lot.ring[0], n2) < Reach + 300f) lots.Add(lot.ring);
            var trees = new List<Vector3>();
            foreach (long key in mt.FittedKeys)
            {
                var plants = mt.PlantsOf(key); if (plants == null) continue;
                foreach (var p in plants)
                    if (Vector2.Distance(new Vector2(p.pos.x, p.pos.z), n2) < Reach + 20f)
                        trees.Add(new Vector3(p.pos.x, p.pos.z, MaxR(kit, p.variant) * p.scale));
            }
            float RoadGap(Vector2 p, out Vector2 away)
            {
                float best = float.MaxValue; away = Vector2.zero;
                for (int i = 0; i < segA.Count; i++)
                {
                    float d = T4SegDist(p, segA[i], segB[i]) - segH[i];
                    if (d < best)
                    {
                        best = d;
                        var a = segA[i]; var dd = segB[i] - a; float L2 = dd.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, dd) / L2) : 0f;
                        away = p - (a + dd * t);
                    }
                }
                return best;
            }
            float BldGap(Vector2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i < fps.Count; i++)
                {
                    float dc = Vector2.Distance(p, fps[i].centre);
                    if (dc - fpR[i] > best) continue;
                    if (T4Inside(fps[i].pts, p)) return 0f;
                    var pts = fps[i].pts;
                    for (int a = pts.Length - 1, b = 0; b < pts.Length; a = b++) best = Mathf.Min(best, T4SegDist(p, pts[a], pts[b]));
                }
                return best;
            }
            bool InLot(Vector2 p) { foreach (var ring in lots) if (T4Inside(ring, p)) return true; return false; }
            float TreeGap(Vector2 p) { float best = float.MaxValue; foreach (var t in trees) best = Mathf.Min(best, Vector2.Distance(p, new Vector2(t.x, t.y)) - t.z * 0.5f); return best; }
            int tried = 0;
            for (float rad = 10f; rad <= 200f; rad += 5f)
                for (float ang = 0f; ang < 360f; ang += 15f)
                {
                    tried++;
                    var c = n2 + new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * rad;
                    if (RoadGap(c, out var away) < 7f || away.sqrMagnitude < 1e-4f) continue;
                    var f = away.normalized;
                    var rt = new Vector2(f.y, -f.x);
                    bool ok = true;
                    float minRoad = float.MaxValue, minCamRoad = float.MaxValue;
                    float gy0 = T4Ground(new Vector3(c.x, 0f, c.y), near.y), gMin = gy0, gMax = gy0;
                    foreach (float gap in new[] { 2.6f, 5.5f })
                        for (int k = -1; k <= 1 && ok; k++)
                        {
                            var p = c + rt * (k * gap);
                            float rg = RoadGap(p, out _);
                            minRoad = Mathf.Min(minRoad, rg);
                            if (rg < 5.5f || BldGap(p) < 7f || InLot(p) || TreeGap(p) < 6f) ok = false;
                            float gy = T4Ground(new Vector3(p.x, 0f, p.y), near.y); gMin = Mathf.Min(gMin, gy); gMax = Mathf.Max(gMax, gy);
                        }
                    if (!ok) continue;
                    foreach (float D in new[] { 3f, 8f })
                    {
                        var q = c - f * D;
                        float rg = RoadGap(q, out _);
                        minCamRoad = Mathf.Min(minCamRoad, rg);
                        if (rg < 1.5f || BldGap(q) < 1f || InLot(q) || TreeGap(q) < 2f) { ok = false; break; }
                        float gy = T4Ground(new Vector3(q.x, 0f, q.y), near.y); gMin = Mathf.Min(gMin, gy); gMax = Mathf.Max(gMax, gy);
                    }
                    if (!ok || gMax - gMin > 1.0f) continue;
                    centre = new Vector3(c.x, gy0, c.y);
                    dir = new Vector3(f.x, 0f, f.y);
                    how = $"{rad:0} m from the place, trunks {minRoad:0.0} m or more from the pavement's edge, camera {minCamRoad:0.0} m or more, ground level within {gMax - gMin:0.0} m, {tried} spots tried";
                    return true;
                }
            how = $"{tried} spots tried";
            return false;
        }

        /// <summary>WHICH TREE IS WHERE in a frame: every planted tree in view - its foot and top on the
        /// saved frame (px, 1280 x 720, y down), its distance, its model, its summer leaf and card
        /// materials, and the level the game draws it at from this eye (MEDIUM: mesh 0-2 or card).</summary>
        static void T4Labels(Camera cam, CityMeshTrees mt, CityTreeKit kit, string path, Vector3 eye, Quaternion rot)
        {
            const float W = 1280f, Hh = 720f;
            float th = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), asp = W / Hh;
            var inv = Quaternion.Inverse(rot);
            var sb = new StringBuilder("model,x_foot,y_foot,y_top,dist_m,level,summer_leaf,summer_card\n");
            var F = mt.Forest;
            foreach (long key in mt.FittedKeys)
            {
                var plants = mt.PlantsOf(key); if (plants == null) continue;
                foreach (var p in plants)
                {
                    var v = kit.variants[p.variant];
                    var lf = inv * (p.pos - eye);
                    var lt = inv * (p.pos + Vector3.up * v.height * p.scale - eye);
                    if (lf.z < 1f) continue;
                    float x = (lf.x / (lf.z * th * asp) * 0.5f + 0.5f) * W;
                    float yf = (1f - (lf.y / (lf.z * th) * 0.5f + 0.5f)) * Hh, yt = (1f - (lt.y / (lt.z * th) * 0.5f + 0.5f)) * Hh;
                    if (x < -40f || x > W + 40f || yt > Hh || yf < 0f) continue;
                    float d = Vector3.Distance(p.pos, eye);
                    string lv = d < F.lod0To ? "mesh0" : d < F.lod1To ? "mesh1" : d < F.meshRange ? "mesh2" : "card";
                    string leaf = v.leafDress != null && v.leafDress.Length > 2 && v.leafDress[2] != null ? v.leafDress[2].name : "-";
                    string card = v.impostorDress != null && v.impostorDress.Length > 2 && v.impostorDress[2] != null ? v.impostorDress[2].name : "-";
                    sb.Append(kit.names[p.variant]).Append(',').Append(x.ToString("0")).Append(',').Append(yf.ToString("0")).Append(',').Append(yt.ToString("0")).Append(',')
                      .Append(d.ToString("0")).Append(',').Append(lv).Append(',').Append(leaf).Append(',').Append(card).Append('\n');
                }
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
