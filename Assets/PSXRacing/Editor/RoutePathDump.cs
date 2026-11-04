using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Five Points round 5, item 1: EVERY CITY RACE PATH, as CityMode.BuildPath
    /// lays it, hashed waypoint by waypoint (bit-exact), with CityAudit's route
    /// pass applied to each (path length within 0.5% of the route, ring
    /// closure, waypoint grade under 16%, waypoints on the solved road) and the
    /// route's joins inside the Five Points circle (90 m round (-3725, 6342)).
    /// Compiles against main's code too, so the same tool runs on both sides.
    /// Writes PSX_ROUTEDUMP_OUT (default &lt;project&gt;/route_paths.txt).
    /// </summary>
    public static class RoutePathDump
    {
        static readonly Vector2 FpCentre = new Vector2(-3725f, 6342f);
        const float FpR = 90f;

        public static void Run()
        {
            var outPath = System.Environment.GetEnvironmentVariable("PSX_ROUTEDUMP_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(Directory.GetCurrentDirectory(), "route_paths.txt");
            var map = CityMap.Get();
            var sb = new StringBuilder();
            int routes = 0, fails = 0;
            sb.AppendLine("id\tcount\thash\tpathM\trouteM\tlenOk\tclose\tgradePct\tgradeOk\tfloating\tjudged\tfloatOk\tfpJoins");
            foreach (var r in map.routes)
            {
                routes++;
                int fpJoins = 0;
                for (int k = 1; k < r.edges.Length; k++)
                {
                    var e = map.edges[r.edges[k]];
                    int jn = r.dirs[k] >= 0 ? e.a : e.b;
                    if ((map.nodes[jn] - FpCentre).sqrMagnitude <= FpR * FpR) fpJoins++;
                }
                var go = new GameObject("~routeDump");
                var tp = go.AddComponent<TrackPath>();
                CityMode.BuildPath(map, r, tp);
                uint h = 2166136261u;
                void Mix(float f) { uint b = (uint)System.BitConverter.SingleToInt32Bits(f); for (int q = 0; q < 4; q++) { h ^= (b >> (8 * q)) & 0xFF; h *= 16777619u; } }
                for (int i = 0; i < tp.Count; i++) { var w = tp.waypoints[i]; Mix(w.x); Mix(w.y); Mix(w.z); }
                if (tp.curvatures != null) foreach (var c in tp.curvatures) Mix(c);
                float pathM = tp.Count * tp.spacing;
                bool lenOk = tp.Count > 500 && Mathf.Abs(pathM - r.lengthM) < Mathf.Max(TrackCatalog.Spacing * 2f, r.lengthM * 0.005f);
                string close = "-";
                if (r.loop) close = Vector3.Distance(tp.waypoints[tp.Count - 1], tp.waypoints[0]) <= tp.spacing * 1.5f ? "ok" : "FAIL";
                float worst = 0f;
                for (int i = 1; i < tp.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(tp.waypoints[i].y - tp.waypoints[i - 1].y) / tp.spacing);
                bool gradeOk = worst < 0.16f;
                var routeEdges = new HashSet<int>(r.edges);
                int floating = 0, judged = 0;
                for (int i = 0; i < tp.Count; i++)
                {
                    var wp = tp.waypoints[i];
                    if (!map.NearestRoadPoint(new Vector2(wp.x, wp.z), 12f, false, out int wei, out float wat, out _)) continue;
                    if (!routeEdges.Contains(wei)) continue;
                    judged++;
                    if (Mathf.Abs(wp.y - map.edges[wei].YAt(wat)) > 0.15f) floating++;
                }
                bool floatOk = floating == 0 && judged > tp.Count / 2;
                if (!lenOk || close == "FAIL" || !gradeOk || !floatOk) fails++;
                sb.AppendLine($"{r.id}\t{tp.Count}\t{h:x8}\t{pathM:0.0}\t{r.lengthM:0.0}\t{(lenOk ? "ok" : "FAIL")}\t{close}\t{worst * 100f:0.00}\t{(gradeOk ? "ok" : "FAIL")}\t{floating}\t{judged}\t{(floatOk ? "ok" : "FAIL")}\t{fpJoins}");
                Object.DestroyImmediate(go);
            }
            sb.AppendLine($"ROUTES {routes} FAILING {fails}");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[ROUTEDUMP] {routes} routes, {fails} failing the route pass -> {outPath}");
        }
    }
}
