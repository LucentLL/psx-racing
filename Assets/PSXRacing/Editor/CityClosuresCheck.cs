using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CLOSURES' ROUTE CHECK WITHOUT A RACE (batch, hidden; owner 2026-11-16: closures only in a
    /// sanctioned event, like a parade, and never on the race line). For every Charlotte race route (the
    /// export's own and those of Resources/CityTracks/*.json): the race line exactly as a race scene builds
    /// it (CityMode.BuildPath into a TrackPath), a sanctioned event's closures built against it
    /// (CityRaceClosures.Build, the registrar's call), and the race play check's own audit
    /// (CityRaceClosures.Audit). 0 rows on the driven lanes is the only pass. The first rule (the map's
    /// pavement only, no race line) is audited beside it, for the record. Also lists every Charlotte track
    /// venue (TrackCatalog.All index, route, host scene) and any the integrator's _order.txt does not list
    /// yet (WARN, not a failure: a lane's new track; tools/city/tracks/order.mjs --write at integration).
    ///   Unity -batchmode -quit -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.CityClosuresCheck.Run
    /// Report: &lt;project&gt;/PSXRacing_closures_check.txt. Exit code 1 on any hit.
    /// </summary>
    public static class CityClosuresCheck
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            sb.AppendLine("CLOSURES ROUTE CHECK " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            try
            {
                var map = CityMap.Get();
                if (map == null) { sb.AppendLine("FAIL no city map"); fails++; }
                else
                {
                    CityTracks.Register(map);
                    var listed = new System.Collections.Generic.HashSet<string>(CityTracks.Order);
                    for (int i = 0; i < TrackCatalog.All.Length; i++)
                    {
                        var t = TrackCatalog.All[i];
                        if (string.IsNullOrEmpty(t.hostScene)) continue;
                        bool known = map.RouteById(t.cityRoute) != null;
                        if (!known) fails++;
                        sb.AppendLine((known ? "venue " : "FAIL  ") + "#" + i + " " + t.id + (t.Reversed ? " (reverse)" : "") + ": route " + t.cityRoute +
                                      (known ? "" : " NOT REGISTERED") + ", in " + TrackCatalog.SceneIdOf(i) + "'s scene, " +
                                      (t.loop ? t.laps + " lap(s) of " : "") + (t.RaceMeters / 1000f).ToString("0.00") + " km raced" +
                                      (listed.Contains(t.id) ? "" : "   WARN not in _order.txt yet (the integrator: node tools/city/tracks/order.mjs --write)"));
                    }
                    foreach (var route in map.routes)
                    {
                        if (route == null || route.edges == null || route.edges.Length == 0) continue;
                        var go = new GameObject("ClosuresCheck_" + route.id);
                        try
                        {
                            var path = go.AddComponent<TrackPath>();
                            CityMode.BuildPath(map, route, path);
                            if (path.waypoints == null || path.waypoints.Length < 2)
                            {
                                sb.AppendLine("FAIL " + route.id + ": no race line built");
                                fails++;
                                continue;
                            }
                            // the first rule: the map's pavement only
                            CityRaceClosures.Build(map, route, go.transform, null);
                            int before = CityRaceClosures.Audit(path, map, route, out string worstBefore);
                            // the rule a race scene uses: against the race line it drives
                            CityRaceClosures.Build(map, route, go.transform, path);
                            int hits = CityRaceClosures.Audit(path, map, route, out string worst);
                            if (hits > 0) fails++;
                            sb.AppendLine((hits == 0 ? "ok   " : "FAIL ") + route.id + ": " + path.waypoints.Length + " waypoints, road " +
                                          path.roadWidth.ToString("0.0") + " m; " + CityRaceClosures.LastClosed + " roads closed, " +
                                          CityRaceClosures.LastLeftOpen + " left open beside the course; " + hits +
                                          " on the race line; nearest: " + worst);
                            sb.AppendLine("     (the first rule, map pavement only: " + before + " on the race line; nearest: " + worstBefore + ")");
                        }
                        finally { Object.DestroyImmediate(go); }
                    }
                }
            }
            catch (System.Exception e)
            {
                sb.AppendLine("FAIL exception: " + e);
                fails++;
            }
            CityRaceClosures.ClearLast();
            sb.AppendLine(fails == 0 ? "ALL CLEAR" : fails + " FAILURE(S)");
            string report = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_closures_check.txt");
            File.WriteAllText(report, sb.ToString());
            Debug.Log("[ClosuresCheck]\n" + sb);
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}
