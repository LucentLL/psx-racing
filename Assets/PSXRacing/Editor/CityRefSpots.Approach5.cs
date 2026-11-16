using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE APPROACH, MEASURED (trees round 5, 2026-11-16; the owner: "Updated
    /// (non-Unity Asset) trees. In particular, less foliage on lower LOD trees
    /// to reduce pop-in visibility"; he has reported tree pop-in on every
    /// forest setting since 11-11). ONE KIT A PROCESS: what PSX_CT_CHARLOTTE
    /// says at start (0 = round 3's NatureManufacture trees, what PC46 ships;
    /// 1 = the owner's Codex trees, kit built with PSX_CT_R4KIT=1).
    ///
    /// At each place (round 3's poses: Providence Rd, West Trade St) the camera
    /// walks a straight line at driver height toward a point 300 m ahead, from
    /// 300 m to 20 m in 10 m steps, on MEDIUM and HIGH forest detail, noon,
    /// summer. Every step is shot TWICE from the same eye: the forest's levels
    /// as the game picks them (a), and as it picked them 10 m earlier
    /// (MeshForest.LodEye, b). Every pixel that differs between a and b is
    /// what changed in those 10 m by LOD selection alone - the pop (counted
    /// offline, approach5.py). Then, for the owner's kit, the staged lawn
    /// comparison at 15 m and 50 m (T4Close, PSX_CT5_CLOSE / PSX_CT5_TRIO).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunApproach5
    ///   PSX_CT4_TAG=nm|codex  PSX_CT5_PLACES=arterial,wtrade  PSX_CT5_LEVELS=1,2
    /// Output: Screenshots/ctrees5/&lt;tag&gt;/&lt;place&gt;_&lt;MEDIUM|HIGH&gt;_&lt;d&gt;_a|b.png, ctrees5_&lt;tag&gt;_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        // ------------------------------------------------------------------
        //  GROUND CONTACT (round 5b; the owner: "Some even seem to be floating in air")
        // ------------------------------------------------------------------
        static readonly Dictionary<int, float[]> t5Base = new Dictionary<int, float[]>();

        /// <summary>A variant's trunk base per level (the lowest bark, m in the tree's frame at scale 1).</summary>
        static float[] T5Base(CityTreeKit kit, int v)
        {
            if (t5Base.TryGetValue(v, out var b)) return b;
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
            t5Base[v] = b;
            return b;
        }

        /// <summary>The highest surface under a point within 4 m above / 8 m below (trunk capsules left out).</summary>
        static float T5GroundY(Vector3 p)
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

        /// <summary>Every planted tree of the ring: its trunk's base at each level (the far card is made from
        /// LOD2) against the ground under the trunk's middle and the lowest ground 0.45 m round it. A tree
        /// FLOATS when its base stands more than 5 cm over the ground under it, or 25 cm over the ground on
        /// one side (a slope).</summary>
        static void T5GroundAudit(CityMeshTrees mt, CityTreeKit kit, string pid, string tag, string root, StringBuilder log)
        {
            Physics.SyncTransforms();
            int n = 0, noGround = 0;
            var flo = new int[3]; var side = new int[3];
            var worst = new List<(float gap, string line)>();
            var csv = new StringBuilder("place,kit,model,lat_lon,scale,level,trunk_base_m,ground_m,gap_m,side_gap_m\n");
            foreach (long key in mt.FittedKeys)
            {
                var plants = mt.PlantsOf(key); if (plants == null) continue;
                foreach (var pp in plants)
                {
                    n++;
                    float gC = T5GroundY(pp.pos);
                    if (float.IsNaN(gC)) { noGround++; continue; }
                    float gMin = gC;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI * 0.5f;
                        float g = T5GroundY(pp.pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.45f);
                        if (!float.IsNaN(g)) gMin = Mathf.Min(gMin, g);
                    }
                    var b = T5Base(kit, pp.variant);
                    for (int l = 0; l < 3; l++)
                    {
                        float bottom = pp.pos.y + b[l] * pp.scale;
                        float gap = bottom - gC, sg = bottom - gMin;
                        bool fl = gap > 0.05f, sd = sg > 0.25f;
                        if (fl) flo[l]++;
                        if (sd) side[l]++;
                        if (fl || sd)
                        {
                            string ll = CityAudit.LatLon(pp.pos.x, pp.pos.z);
                            csv.Append(pid).Append(',').Append(tag).Append(',').Append(kit.names[pp.variant]).Append(',').Append(ll.Replace(",", " ")).Append(',')
                               .Append(pp.scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(l).Append(',')
                               .Append(b[l].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(gC.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                               .Append(gap.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(sg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                            if (l == 0 || fl) worst.Add((Mathf.Max(gap, sg - 0.2f), $"{kit.names[pp.variant]} at {ll}: LOD{l} base {b[l]:0.00} m x {pp.scale:0.00}, over the ground by {gap:0.00} m (one side {sg:0.00} m)"));
                        }
                    }
                }
            }
            worst.Sort((x, y) => y.gap.CompareTo(x.gap));
            log.Append($"  GROUND CONTACT {pid} ({tag}): {n} planted trees ({noGround} with no ground found under them); floating (base > 5 cm over the ground under the trunk) at LOD0 {flo[0]}, LOD1 {flo[1]}, LOD2 + far card {flo[2]}; over a slope (> 25 cm on one side) at LOD0 {side[0]}, LOD1 {side[1]}, LOD2 {side[2]}\n");
            for (int i = 0; i < Mathf.Min(10, worst.Count); i++) log.Append("    " + worst[i].line + "\n");
            File.WriteAllText(Path.Combine(root, $"ground_{tag}_{pid}.csv"), csv.ToString());
        }

        public static void RunApproach5()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_CT4_TAG");
            if (string.IsNullOrEmpty(tag)) tag = CityMeshTrees.Charlotte ? "codex" : "nm";
            var log = new StringBuilder($"CITY TREES APPROACH 5 ({tag}: PSX_CT_CHARLOTTE {(CityMeshTrees.Charlotte ? "on" : "OFF")}) " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "ctrees5");
            string dir = Path.Combine(root, tag);
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var staged = new List<GameObject>();
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kitAsset = PSXRacingBuilder.EnsureCityTreeKit(System.Environment.GetEnvironmentVariable("PSX_CT_REKIT") == "1");
                if (kitAsset == null) throw new System.Exception("no city tree kit (store files?)");
                log.Append($"kit ready at {clock.Elapsed.TotalSeconds:0} s\n");
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
                var mt = CityMeshTrees.EnsureFor(world);
                if (mt == null) throw new System.Exception("CityMeshTrees did not start (kit / GPU)");
                var kit = mt.Kit;
                if (CityMeshTrees.Charlotte && !mt.CharlotteReady) throw new System.Exception("PSX_CT_CHARLOTTE on but the kit has no Charlotte forms (PSX_CT_R4KIT=1)");
                log.Append($"kit: {kit.variants.Length} species; GPU tree buffers {T4GpuBytes(mt.Forest) / 1048576.0:0.0} MB\n");
                string[] places = (System.Environment.GetEnvironmentVariable("PSX_CT5_PLACES") ?? "arterial,wtrade").Split(',');
                string[] levels = (System.Environment.GetEnvironmentVariable("PSX_CT5_LEVELS") ?? "1,2").Split(',');
                foreach (var pid in places)
                {
                    int pi = System.Array.FindIndex(T3Places, q => q.id == pid);
                    if (pi < 0) { log.Append(pid + ": unknown place\n"); continue; }
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
                    // the walk: from 300 m before the pose's eye to 20 m before it, the trees round it coming in through every band
                    var target = eye;
                    world.EnsureRing(target - f * 160f, 2); mt.Flush();
                    log.Append($"\n{pid}\t'{e.name}' {CityAudit.LatLon(target.x, target.z)} heading ({f.x:0.00}, {f.z:0.00}); {how}\n");
                    T5GroundAudit(mt, kit, pid, tag, root, log);
                    // THE PROBE (round 5b): the frame at PSX_CT5_PROBE metres, every tree in it labelled, and the three largest
                    // trees left of the road shot at their trunk's base from 8 m (the owner's circled trees)
                    if (float.TryParse(System.Environment.GetEnvironmentVariable("PSX_CT5_PROBE") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pd))
                    {
                        ForestDetailPrefs.Override = 1; mt.Rebuild();
                        T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer);
                        var q0 = target - f * pd;
                        var ppos = new Vector3(q0.x, T4Ground(q0, target.y - 1.15f) + 1.15f, q0.z);
                        var prot = Quaternion.LookRotation(f + Vector3.down * 0.03f);
                        if (car != null) car.TeleportTo(ppos - f * 6f - Vector3.up * 0.8f, Quaternion.LookRotation(f));
                        cam.transform.SetPositionAndRotation(ppos, prot);
                        SunShadows.RenderFor(cam); MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                        PSXScreenshotTool.ShotAs(cam, $"ctrees5/{tag}/{pid}_probe_{pd:0}", ppos, prot);
                        T4Labels(cam, mt, kit, Path.Combine(dir, $"{pid}_probe_{pd:0}_labels.csv"), ppos, prot);
                        float th = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), asp = 1280f / 720f;
                        var inv = Quaternion.Inverse(prot);
                        var cands = new List<(float px, MeshForest.PoolPlant pl, float sx)>();
                        foreach (long key in mt.FittedKeys)
                        {
                            var pls = mt.PlantsOf(key); if (pls == null) continue;
                            foreach (var pp in pls)
                            {
                                var lf = inv * (pp.pos - ppos);
                                if (lf.z < 3f || lf.z > 90f) continue;
                                float sx = (lf.x / (lf.z * th * asp) * 0.5f + 0.5f) * 1280f;
                                if (sx < 0f || sx > 560f) continue;
                                float hpx = kit.variants[pp.variant].height * pp.scale / (lf.z * th) * 360f;
                                cands.Add((hpx, pp, sx));
                            }
                        }
                        cands.Sort((x, y) => y.px.CompareTo(x.px));
                        for (int k = 0; k < Mathf.Min(3, cands.Count); k++)
                        {
                            var pp = cands[k].pl;
                            var away = new Vector3(ppos.x - pp.pos.x, 0f, ppos.z - pp.pos.z).normalized;
                            var cpos = pp.pos + away * 8f; cpos.y = T4Ground(cpos, pp.pos.y) + 1.2f;
                            var crot = Quaternion.LookRotation((pp.pos + Vector3.up * 1.6f) - cpos);
                            cam.transform.SetPositionAndRotation(cpos, crot);
                            SunShadows.RenderFor(cam); mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctrees5/{tag}/{pid}_probe_close{k}", cpos, crot);
                            float gC = T5GroundY(pp.pos);
                            log.Append($"  PROBE {pid} close{k}: {kit.names[pp.variant]} at {CityAudit.LatLon(pp.pos.x, pp.pos.z)} (screen x {cands[k].sx:0}, {cands[k].px:0} px tall), foot {pp.pos.y:0.00} m, physics ground {gC:0.00} m, trunk base {pp.pos.y + T5Base(kit, pp.variant)[0] * pp.scale:0.00} m\n");
                        }
                        ForestDetailPrefs.Override = -1; mt.Rebuild();
                    }
                    if (System.Environment.GetEnvironmentVariable("PSX_CT5_WALK") == "0") continue;
                    foreach (var ls in levels)
                    {
                        int lvl = int.Parse(ls);
                        ForestDetailPrefs.Override = lvl;
                        mt.Rebuild();
                        string L = ForestDetailPrefs.Label;
                        T4Light(sun, dressing, world, TimeOfDay.Noon, (int)Season.Summer);
                        log.Append($"  {L}: levels {mt.Forest.lod0To:0} / {mt.Forest.lod1To:0} / {mt.Forest.meshRange:0} m (cards past), {mt.LivePlants()} plants live\n");
                        for (int d = 300; d >= 20; d -= 10)
                        {
                            var q = target - f * d;
                            var pos = new Vector3(q.x, T4Ground(q, target.y - 1.15f) + 1.15f, q.z);
                            var rot = Quaternion.LookRotation(f + Vector3.down * 0.03f);
                            if (car != null) car.TeleportTo(pos - f * 6f - Vector3.up * 0.8f, Quaternion.LookRotation(f));
                            cam.transform.SetPositionAndRotation(pos, rot);
                            SunShadows.RenderFor(cam);
                            MeshForest.LodEyeOn = false; MeshForest.MeshTreesOn = true; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctrees5/{tag}/{pid}_{L}_{d:000}_a", pos, rot);
                            MeshForest.LodEyeOn = true; MeshForest.LodEye = pos - f * 10f; mt.Forest.DrawNow(cam);
                            PSXScreenshotTool.ShotAs(cam, $"ctrees5/{tag}/{pid}_{L}_{d:000}_b", pos, rot);
                            MeshForest.LodEyeOn = false;
                            shots += 2;
                        }
                    }
                    ForestDetailPrefs.Override = -1;
                    mt.Rebuild();
                    // the owner's trees beside a NatureManufacture tree, staged on a lawn (not planting)
                    if (CityMeshTrees.Charlotte && pid == "arterial") shots += T4Close(cam, sun, dressing, world, mt, map, kit, "../ctrees5/" + tag, eye, staged, log);
                }
                log.Append($"\nswapped like for like (PSX_CT5_SWAP): {CityMeshTrees.Swapped5}\n{shots} frames to {dir}; {clock.Elapsed.TotalSeconds:0} s\nCITY TREES APPROACH 5 OK\n");
            }
            catch (System.Exception ex) { log.Append("CITY TREES APPROACH 5 FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                foreach (var g in staged) if (g != null) Object.DestroyImmediate(g);
                ForestDetailPrefs.Override = -1;
                MeshForest.LodEyeOn = false;
                File.WriteAllText(Path.Combine(root, $"ctrees5_{tag}_log.txt"), log.ToString());
                Debug.Log("[CityTrees5]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
