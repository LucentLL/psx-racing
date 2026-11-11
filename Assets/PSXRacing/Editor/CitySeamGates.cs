using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD SLAB'S GATES in one editor (owner 2026-11-11): CityAudit.RunDrive
    /// on the uptown route and on the owner's North Graham Street junction
    /// (PSX_SEAMGATE_EDGES, default e20287), and CityRoadMeshHash, each with the
    /// road slab off and on (CityMeshes.RoadSlabOn, tiles built live, the tile
    /// cache off). Copies of each report at the project root:
    /// city_drive_&lt;scope&gt;_&lt;before|after&gt;.txt, road_mesh_hash_&lt;before|after&gt;.txt.
    ///   -executeMethod PSXRacing.EditorTools.CitySeamGates.Run  (-quit)
    /// PSX_SEAMGATE_SKIP=drive|hash skips one.
    /// </summary>
    public static class CitySeamGates
    {
        /// <summary>The seam audit, then the gates.</summary>
        public static void RunWithAudit()
        {
            CitySeamAudit.NoExit = true;
            try { CitySeamAudit.Run(); } finally { CitySeamAudit.NoExit = false; }
            Run();
        }

        public static void Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var log = new StringBuilder("SEAM GATES " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            bool slabWas = CityMeshes.RoadSlabOn, cacheWas = CityTileCache.Enabled;
            string skip = System.Environment.GetEnvironmentVariable("PSX_SEAMGATE_SKIP") ?? "";
            string ownerEdges = System.Environment.GetEnvironmentVariable("PSX_SEAMGATE_EDGES");
            if (string.IsNullOrEmpty(ownerEdges)) ownerEdges = "20287";
            try
            {
                CityTileCache.Enabled = false;
                System.Environment.SetEnvironmentVariable("PSX_DRIVE_RAILBOX", "0");
                System.Environment.SetEnvironmentVariable("PSX_DRIVE_BARRIERS", "0");
                foreach (var mode in new[] { "before", "after" })
                {
                    CityMeshes.RoadSlabOn = mode == "after";
                    if (!skip.Contains("drive"))
                        foreach (var (scope, routes, edges) in new[] { ("uptown", "uptown", ""), ("owner", "", ownerEdges) })
                        {
                            System.Environment.SetEnvironmentVariable("PSX_DRIVE_ROUTES", routes);
                            System.Environment.SetEnvironmentVariable("PSX_DRIVE_EDGES", edges);
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            try { CityAudit.RunDrive(); }
                            catch (System.Exception ex) { log.Append($"drive {scope} {mode} FAILED {ex}\n"); }
                            string src = Path.Combine(root, "city_drive.txt"), dst = Path.Combine(root, $"city_drive_{scope}_{mode}.txt");
                            if (File.Exists(src)) { File.Copy(src, dst, true); File.Delete(src); }
                            log.Append($"drive {scope} {mode}: {clock.Elapsed.TotalSeconds:0} s -> {dst}\n");
                        }
                    if (!skip.Contains("hash"))
                    {
                        CityRoadMeshHash.Run();
                        string src = Path.Combine(root, "PSXRacing_road_mesh_hash.txt"), dst = Path.Combine(root, $"road_mesh_hash_{mode}.txt");
                        if (File.Exists(src)) { File.Copy(src, dst, true); File.Delete(src); }
                        log.Append($"hash {mode} -> {dst}\n");
                    }
                }
                log.Append("SEAM GATES DONE\n");
            }
            catch (System.Exception ex) { log.Append("SEAM GATES FAILED " + ex + "\n"); }
            finally
            {
                CityMeshes.RoadSlabOn = slabWas; CityTileCache.Enabled = cacheWas;
                File.WriteAllText(Path.Combine(root, "seam_gates_log.txt"), log.ToString());
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
