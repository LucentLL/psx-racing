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
                        shots += T4Close(cam, sun, dressing, world, mt, kit, dir, tag, eye, fwd, staged, log);
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
        /// willow oak, a young loblolly and a small Forest beech stood side by side on the road ahead
        /// (staged, not planted), at 3 m and at 8 m, noon, summer.</summary>
        static int T4Close(Camera cam, Light sun, SeasonDress dressing, CityWorld world, CityMeshTrees mt, CityTreeKit kit, string dir, string tag,
                           Vector3 eye, Vector3 fwd, List<GameObject> staged, StringBuilder log)
        {
            int Find(string n) => kit.names != null ? System.Array.IndexOf(kit.names, n) : -1;
            int oak = Find("CT4_WillowOak_YoungStreet"), pine = Find("CT4_LoblollyPine_Young"), beech = -1;
            for (int v = 0; v < kit.variants.Length && beech < 0; v++) if (kit.Group3(v) == 0 && kit.roles[v] == CityTreeKit.Role.Small) beech = v;
            if (oak < 0 || pine < 0 || beech < 0) { log.Append($"  CLOSE: a model missing (oak {oak}, pine {pine}, beech {beech})\n"); return 0; }
            T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer);
            var f = new Vector3(fwd.x, 0f, fwd.z).normalized;
            var right = Vector3.Cross(Vector3.up, f);
            float ground = eye.y - 1.15f;
            int n = 0;
            foreach (float D in new[] { 3f, 8f })
            {
                foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
                staged.Clear();
                float gap = D < 5f ? 2.6f : 5.5f;
                var at = eye + f * (D + 2.5f);
                int[] which = { oak, pine, beech };
                for (int k = 0; k < 3; k++)
                {
                    var v = kit.variants[which[k]];
                    var go = new GameObject("T4Close_" + kit.names[which[k]]) { hideFlags = HideFlags.DontSave };
                    var pos = new Vector3(at.x, ground, at.z) + right * ((k - 1) * gap);
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
                var camPos = new Vector3(eye.x, ground + 1.6f, eye.z) + f * 2.5f;
                var r = Quaternion.LookRotation(f + Vector3.up * (D < 5f ? 0.32f : 0.22f));
                cam.transform.SetPositionAndRotation(camPos, r);
                SunShadows.RenderFor(cam);
                MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                PSXScreenshotTool.ShotAs(cam, $"ctrees4/{tag}/close_{D:0}m", camPos, r);
                n++;
            }
            foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
            staged.Clear();
            log.Append($"  CLOSE: {kit.names[oak]} (left), {kit.names[pine]} (middle), {kit.names[beech]} (right, NatureManufacture Forest), 3 m and 8 m\n");
            return n;
        }
    }
}
