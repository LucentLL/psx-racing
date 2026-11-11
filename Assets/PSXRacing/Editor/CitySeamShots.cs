using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SEAM PICTURES (owner 2026-11-11): the six junction places of
    /// <see cref="CitySeamPlaces"/>, each from the driver's eye (1.1 m over
    /// the right-hand lane) 15 m and 40 m short of the seam (the fan's mouth
    /// on the approach arm), looking at it, and from a grazing chase camera
    /// (1.0 m up, 25 m back); at noon and at sunset; with the road slab off
    /// (before) and on (after), tiles rebuilt live between (the tile cache
    /// off). Through the game's own path (Charlotte.unity opened, TimeOfDay,
    /// PSXScreenshotTool.ShotAs) at 720 lines.
    ///   -executeMethod PSXRacing.EditorTools.CitySeamShots.Run
    ///   PSX_SEAMSHOT_ONLY=id,id  PSX_SEAMSHOT_HOURS=noon,sunset  PSX_SEAMSHOT_MODES=before,after
    /// Output: Screenshots/seams/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;mode&gt;.png + seams_log.txt.
    /// </summary>
    public static class CitySeamShots
    {
        /// <summary>The seam audit (city_seam_audit.txt), then the pictures.</summary>
        public static void RunAll()
        {
            CitySeamAudit.NoExit = true;
            try { CitySeamAudit.Run(); } finally { CitySeamAudit.NoExit = false; }
            Run();
        }

        public static void Run()
        {
            var log = new StringBuilder("SEAM SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "seams");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            bool slabWas = CityMeshes.RoadSlabOn, cacheWas = CityTileCache.Enabled;
            try
            {
                CityTileCache.Enabled = false;   // live builds: the switch decides what is drawn
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = 720;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var trims = CityMeshes.ComputeTrims(map);
                var places = CitySeamPlaces.Find(map, trims, log);
                string only = System.Environment.GetEnvironmentVariable("PSX_SEAMSHOT_ONLY");
                var hours = Pick("PSX_SEAMSHOT_HOURS", "noon,sunset");
                var modes = Pick("PSX_SEAMSHOT_MODES", "before,after");
                foreach (var mode in modes)
                {
                    CityMeshes.RoadSlabOn = mode != "before";
                    foreach (var pl in places)
                    {
                        if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                        float lat = pl.oneway ? 0f : pl.width * 0.25f;
                        Vector3 lane = pl.right * lat;
                        world.DropAll();
                        world.EnsureRing(pl.seam, 2);
                        Physics.SyncTransforms();
                        // the car out of the frame: in the lane behind every camera
                        var carAt = pl.seam - pl.fwd * 60f + lane;
                        carAt.y = Ground(carAt, pl.seam.y) + 0.35f;
                        if (car != null) car.TeleportTo(carAt, Quaternion.LookRotation(pl.fwd)); else player.transform.SetPositionAndRotation(carAt, Quaternion.LookRotation(pl.fwd));
                        var target = pl.seam + lane;
                        target.y = Ground(target, pl.seam.y);
                        var poses = new List<(string name, Vector3 eye)>();
                        foreach (var (nm, back, up) in new[] { ("eye15", 15f, 1.1f), ("eye40", 40f, 1.1f), ("chase", 25f, 1.0f) })
                        {
                            var e = pl.seam - pl.fwd * back + lane;
                            e.y = Ground(e, pl.seam.y) + up;
                            poses.Add((nm, e));
                        }
                        log.Append($"{mode} {pl.id}: seam ({target.x:0.0},{target.y:0.00},{target.z:0.0}) fwd ({pl.fwd.x:0.00},{pl.fwd.z:0.00}) slab tris last tile {CityMeshes.slabTris}\n");
                        foreach (var h in hours)
                        {
                            int hour = h == "noon" ? TimeOfDay.Noon : h == "sunset" ? TimeOfDay.Sunset : h == "dusk" ? TimeOfDay.Dusk : TimeOfDay.Afternoon;
                            RaceHandoff.CalendarDay = 0;
                            TimeOfDay.Apply(hour, sun);
                            var globals = Object.FindAnyObjectByType<PSXGlobals>();
                            if (globals != null) globals.Apply();
                            var hd = TimeOfDay.At(hour);
                            NightGlow.PreviewAll(hd.lightsOn);
                            foreach (var p in poses)
                            {
                                CarLights.PushGlobals();
                                // the seam at the frame's middle
                                var rot = Quaternion.LookRotation(target - p.eye, Vector3.up);
                                PSXScreenshotTool.ShotAs(cam, $"seams/{pl.id}_{p.name}_{h}_{mode}", p.eye, rot);
                                shots++;
                            }
                        }
                    }
                }
                world.DropAll();
                log.Append($"{shots} frames to {dir}\nSEAM SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("SEAM SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshes.RoadSlabOn = slabWas; CityTileCache.Enabled = cacheWas;
                File.WriteAllText(Path.Combine(dir, "seams_log.txt"), log.ToString());
                Debug.Log("[SeamShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static float Ground(Vector3 p, float near)
        {
            // the road's own surface under the point (the highest within 3 m of the seam's height)
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, near + 3f, p.z), Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider.name == "Roads" && hit.point.y > best) best = hit.point.y;
            return float.IsNegativeInfinity(best) ? near : best;
        }

        static string[] Pick(string env, string dflt)
        {
            string v = System.Environment.GetEnvironmentVariable(env);
            return (string.IsNullOrEmpty(v) ? dflt : v).Split(',');
        }
    }
}
