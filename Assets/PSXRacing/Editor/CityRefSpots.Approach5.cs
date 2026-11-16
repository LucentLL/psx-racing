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
                log.Append($"\n{shots} frames to {dir}; {clock.Elapsed.TotalSeconds:0} s\nCITY TREES APPROACH 5 OK\n");
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
