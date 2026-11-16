using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FEET, PROVED (2026-11-16; PSXRacingBuilder.CTFeet). At round 3's
    /// Providence Rd and West Trade St poses, rings of tiles fitted as the game
    /// fits them, then for every planted tree its trunk's base at each level
    /// (LOD0-2, the lowest bark in the tree's frame x its scale) against the
    /// ground under the middle of its trunk and 0.45 m round it: a tree FLOATS
    /// when its base stands more than 5 cm over the ground under it. Then the
    /// approach at Providence Rd on MEDIUM and HIGH (the camera 300 -> 20 m at
    /// driver height, every 10 m shot with the game's levels (a) and with the
    /// levels of 10 m earlier (b): the pop, counted offline).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTreeFeet   PSX_FEET_TAG=..  PSX_FEET_WALK=0
    /// Output: Screenshots/ctreefeet/&lt;tag&gt;/arterial_&lt;MEDIUM|HIGH&gt;_&lt;d&gt;_a|b.png, ctreefeet_&lt;tag&gt;_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        static readonly Dictionary<int, float[]> feetBase = new Dictionary<int, float[]>();

        static float[] FeetBase(CityTreeKit kit, int v)
        {
            if (feetBase.TryGetValue(v, out var b)) return b;
            var vr = kit.variants[v]; b = new float[3];
            for (int l = 0; l < 3; l++)
            {
                float mn = float.MaxValue; var m = vr.lods[l];
                if (m != null)
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        if (vr.leafSub != null && s < vr.leafSub.Length && vr.leafSub[s] && m.subMeshCount > 1) continue;
                        if (!TreeKit.Read(m, s, out var vv, out var tt)) continue;
                        foreach (int i in tt) mn = Mathf.Min(mn, vr.pre.MultiplyPoint3x4(vv[i]).y);
                    }
                b[l] = mn < float.MaxValue ? mn : 0f;
            }
            feetBase[v] = b;
            return b;
        }

        /// <summary>The highest surface under a point within 4 m above / 8 m below (trunk capsules left out).</summary>
        static float FeetGroundY(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 4f, p.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (var h in hits)
            {
                if (h.collider is CapsuleCollider) continue;
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            }
            return best;
        }

        static float FeetGroundAt(Vector3 p, float nearY)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, nearY + 60f, p.z), Vector3.down, 140f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - nearY) < 12f && (float.IsNaN(best) || h.point.y < best)) best = h.point.y;
            return float.IsNaN(best) ? nearY : best;
        }

        static void FeetAudit(CityMeshTrees mt, CityTreeKit kit, string pid, string tag, StringBuilder log)
        {
            Physics.SyncTransforms();
            int n = 0; var flo = new int[3]; var side = new int[3];
            var worst = new List<(float gap, string line)>();
            foreach (long key in mt.FittedKeys)
            {
                var plants = mt.PlantsOf(key); if (plants == null) continue;
                foreach (var pp in plants)
                {
                    float gC = FeetGroundY(pp.pos);
                    if (float.IsNaN(gC)) continue;
                    n++;
                    float gMin = gC;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI * 0.5f;
                        float g = FeetGroundY(pp.pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.45f);
                        if (!float.IsNaN(g)) gMin = Mathf.Min(gMin, g);
                    }
                    var b = FeetBase(kit, pp.variant);
                    for (int l = 0; l < 3; l++)
                    {
                        float bottom = pp.pos.y + b[l] * pp.scale, gap = bottom - gC, sg = bottom - gMin;
                        if (gap > 0.05f) { flo[l]++; worst.Add((gap, $"{kit.names[pp.variant]} at {CityAudit.LatLon(pp.pos.x, pp.pos.z)}: LOD{l} base {b[l]:0.00} m x {pp.scale:0.00}, {gap:0.00} m over the ground")); }
                        if (sg > 0.25f) side[l]++;
                    }
                }
            }
            worst.Sort((x, y) => y.gap.CompareTo(x.gap));
            log.Append($"  GROUND CONTACT {pid} ({tag}): {n} planted trees; floating (base > 5 cm over the ground under the trunk) at LOD0 {flo[0]}, LOD1 {flo[1]}, LOD2 {flo[2]}; over a slope (> 25 cm on one side) at LOD0 {side[0]}, LOD1 {side[1]}, LOD2 {side[2]}\n");
            for (int i = 0; i < Mathf.Min(6, worst.Count); i++) log.Append("    " + worst[i].line + "\n");
        }

        static void FeetLight(Light sun, SeasonDress dressing, CityWorld world)
        {
            RaceHandoff.CalendarDay = 0;
            TimeOfDay.Apply(TimeOfDay.Noon, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            NightGlow.PreviewAll(false);
            foreach (var cl in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) cl.PreviewBuild(false);
            SetDress(dressing, world, (int)Season.Summer);
            CarLights.PushGlobals();
        }

        /// <summary>THE LOD FADE's frame cost in one process (tree pop round,
        /// 2026-11-16): the forest built with the fixed hand-over bands and with
        /// the wide ones in turn at one eye, <paramref name="rounds"/> builds each
        /// in ABBA order (the editor's own noise shows in the spread); mesh trees
        /// drawn, 1920x1080, every frame waited for with a read-back.</summary>
        static void CostAB(Camera cam, CityMeshTrees mt, string place, Vector3 eye, Quaternion r, int rounds, StringBuilder log)
        {
            bool was = CityMeshTrees.LodFade;
            cam.transform.SetPositionAndRotation(eye, r);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var ms = new[] { new List<double>(), new List<double>() };
            var tris = new double[2]; var draws = new double[2]; var plants = new double[2];
            var bands = new string[2];
            try
            {
                for (int k = 0; k < rounds * 2; k++)
                {
                    int m = (k + k / 2) & 1;   // off, on, on, off, off, on, on, off
                    CityMeshTrees.LodFade = m == 1; mt.Rebuild();
                    bands[m] = $"{mt.Forest.band0:0}/{mt.Forest.band1:0}/{mt.Forest.band2:0} m";
                    MeshForest.MeshTreesOn = true;
                    for (int i = 0; i < 4; i++) { mt.Forest.DrawNow(cam); cam.Render(); }
                    const int N = 24;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < N; i++)
                    {
                        mt.Forest.DrawNow(cam); cam.Render();
                        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                    }
                    sw.Stop();
                    ms[m].Add(sw.Elapsed.TotalMilliseconds / N);
                    MeshForest.StatsOn = true; MeshForest.StatsReset();
                    for (int i = 0; i < 6; i++) { mt.Forest.DrawNow(cam); cam.Render(); }
                    UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                    int sf = Mathf.Max(1, MeshForest.StatFrames);
                    tris[m] = MeshForest.SumTris / sf; draws[m] = MeshForest.SumDraws / sf; plants[m] = MeshForest.SumPlants / sf;
                    MeshForest.StatsOn = false;
                }
            }
            finally
            {
                MeshForest.StatsOn = false;
                CityMeshTrees.LodFade = was; mt.Rebuild();
                cam.targetTexture = old;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
            }
            double Med(List<double> l)
            {
                var c = new List<double>(l); c.Sort();
                return c.Count == 0 ? 0 : c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
            }
            string All(List<double> l) => string.Join(" ", l.ConvertAll(x => x.ToString("0.00")));
            double d = Med(ms[1]) - Med(ms[0]);
            log.Append($"  COST A/B {place}: frame (1920x1080, editor, render + read-back, {rounds} builds each, ABBA): fixed bands ({bands[0]}) {Med(ms[0]):0.00} ms [{All(ms[0])}], " +
                       $"LOD fade ({bands[1]}) {Med(ms[1]):0.00} ms [{All(ms[1])}], {(d >= 0 ? "+" : "")}{d:0.00} ms; drawn {plants[0]:0} -> {plants[1]:0} plants, " +
                       $"{tris[0] / 1e6:0.00} -> {tris[1] / 1e6:0.00} M tris, {draws[0]:0} -> {draws[1]:0} draws\n");
        }

        public static void RunTreeFeet()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_FEET_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "feet";
            var log = new StringBuilder($"CITY TREES FEET ({tag}; PSX_CT5_FOOT {System.Environment.GetEnvironmentVariable("PSX_CT5_FOOT") ?? "(default on)"}) " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctreefeet");
            Directory.CreateDirectory(Path.Combine(root, tag));
            int code = 0, shots = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                if (PSXRacingBuilder.EnsureCityTreeKit(true) == null) throw new System.Exception("no city tree kit (store files?)");
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
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                log.Append($"kit: {kit.variants.Length} species; quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', target {EditorUserBuildSettings.activeBuildTarget}\n");
                // THE POP KNOBS (tree pop-in round): the hand-over bands as a share of each level's distance
                // (MeshForest.TourBandShare) and each level's distance x (MeshForest.TourLodMul), for this process
                if (float.TryParse(System.Environment.GetEnvironmentVariable("PSX_LODBAND") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bandShare) && bandShare > 0f)
                    MeshForest.TourBandShare = bandShare;
                var lm = (System.Environment.GetEnvironmentVariable("PSX_LODMUL") ?? "").Split(',');
                if (lm.Length == 3 && float.TryParse(lm[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m0)
                    && float.TryParse(lm[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m1)
                    && float.TryParse(lm[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m2))
                    MeshForest.TourLodMul = new Vector3(m0, m1, m2);
                log.Append($"pop knobs: band share {MeshForest.TourBandShare:0.00} (-1 = the forest's own), level distances x {MeshForest.TourLodMul}\n");
                log.Append($"city LOD fade (PSX_CT_LODFADE={System.Environment.GetEnvironmentVariable("PSX_CT_LODFADE") ?? "(default)"}): {(CityMeshTrees.LodFade ? $"ON, bands {CityMeshTrees.LodFadeShare:0.00} of each level's distance" : "OFF, the fixed bands")}\n");
                var levels = new List<int>();
                foreach (var lv in (System.Environment.GetEnvironmentVariable("PSX_FEET_LEVELS") ?? "1,2").Split(','))
                    if (int.TryParse(lv.Trim(), out int lvi) && lvi >= 0 && lvi <= 2) levels.Add(lvi);
                bool costAB = System.Environment.GetEnvironmentVariable("PSX_FEET_COSTAB") == "1";
                // THE MOTION PAIR (PSX_FEET_MOTION=m): each step also shot from m metres further on with the
                // game's own levels (c): a v c is one frame of driving (0.4 m = 86 km/h at 60 FPS), so the
                // screen-door dither's crawl over a moving crown shows next to the parallax both runs share
                float motionM = 0f;
                float.TryParse(System.Environment.GetEnvironmentVariable("PSX_FEET_MOTION") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out motionM);
                if (motionM > 0f) log.Append($"motion pair: c = {motionM:0.00} m on, the game's levels\n");
                bool walk = System.Environment.GetEnvironmentVariable("PSX_FEET_WALK") != "0";
                foreach (var pid in (System.Environment.GetEnvironmentVariable("PSX_FEET_PLACES") ?? "arterial,wtrade").Split(','))
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) continue;
                    var pl = T3Places[pi];
                    CityMap.Edge e; float s; bool named = true;
                    if (pl.onewayOnly) { if (!T3SnapOneway(map, LL(pl.lat, pl.lon), pl.roads[0], pl.hdg, out e, out s)) { log.Append(pid + ": no divided piece\n"); continue; } }
                    else if (!CtSnap(map, LL(pl.lat, pl.lon), pl.roads, out e, out s, out named)) { log.Append(pid + ": no road near\n"); continue; }
                    var p0 = e.PointAt(s);
                    var anchor = new Vector3(p0.x, e.YAt(s), p0.y);
                    world.DropAll(); mt.Flush();
                    world.EnsureRing(anchor, 2); mt.Flush();
                    var cp = new CtPlace { id = pl.id, lat = pl.lat, lon = pl.lon, roads = pl.roads, hdg = pl.hdg, kind = pl.kind, what = pl.what };
                    if (!CtPose(cp, map, mt, e, s, out var eye, out var fwd, out _, out string how)) { log.Append($"{pid}: no pose ({how})\n"); continue; }
                    var f = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    world.EnsureRing(eye - f * 160f, 2); mt.Flush();
                    log.Append($"\n{pid}\t'{e.name}' {CityAudit.LatLon(eye.x, eye.z)}\n");
                    FeetAudit(mt, kit, pid, tag, log);
                    if (!walk) continue;
                    foreach (int lvl in levels)
                    {
                        ForestDetailPrefs.Override = lvl; mt.Rebuild();
                        string L = ForestDetailPrefs.Label;
                        FeetLight(sun, dressing, world);
                        log.Append($"  {pid} {L}: levels {mt.Forest.lod0To:0} / {mt.Forest.lod1To:0} / {mt.Forest.meshRange:0} m, bands {mt.Forest.band0:0} / {mt.Forest.band1:0} / {mt.Forest.band2:0} m\n");
                        if (pid == "arterial") { var crot = Quaternion.LookRotation(f + Vector3.down * 0.03f); Cost(cam, mt, $"{pid} {L} {tag}", eye - f * 150f + Vector3.up * 0.0f, crot, log); }
                        if (pid == "arterial" && costAB) { var crot = Quaternion.LookRotation(f + Vector3.down * 0.03f); CostAB(cam, mt, $"{pid} {L}", eye - f * 150f, crot, 4, log); }
                        for (int d = 300; d >= 20; d -= 10)
                        {
                            var q = eye - f * d;
                            var pos = new Vector3(q.x, FeetGroundAt(q, eye.y - 1.15f) + 1.15f, q.z);
                            var rot = Quaternion.LookRotation(f + Vector3.down * 0.03f);
                            if (car != null) car.TeleportTo(pos - f * 6f - Vector3.up * 0.8f, Quaternion.LookRotation(f));
                            cam.transform.SetPositionAndRotation(pos, rot);
                            SunShadows.RenderFor(cam);
                            MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctreefeet/{tag}/{pid}_{L}_{d:000}_a", pos, rot);
                            MeshForest.LodEyeOn = true; MeshForest.LodEye = pos - f * 10f; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctreefeet/{tag}/{pid}_{L}_{d:000}_b", pos, rot);
                            MeshForest.LodEyeOn = false;
                            shots += 2;
                            if (motionM > 0f)
                            {
                                var qc = q + f * motionM;
                                var posc = new Vector3(qc.x, FeetGroundAt(qc, eye.y - 1.15f) + 1.15f, qc.z);
                                cam.transform.SetPositionAndRotation(posc, rot);
                                SunShadows.RenderFor(cam);
                                mt.Forest.DrawNow(cam);
                                PSXScreenshotTool.ShotAs(cam, $"ctreefeet/{tag}/{pid}_{L}_{d:000}_c", posc, rot);
                                shots++;
                            }
                        }
                    }
                    ForestDetailPrefs.Override = -1; mt.Rebuild();
                }
                log.Append($"\n{shots} frames; {clock.Elapsed.TotalSeconds:0} s\nCITY TREES FEET OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY TREES FEET FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(root, $"ctreefeet_{tag}_log.txt"), log.ToString());
                Debug.Log("[CityTreeFeet]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
