using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CITY INTEGRATION PICTURES (2026-11-11, cityinteg): the four geometry
    /// branches together (beacons, curbs + drains, road slab, houses + low-rise)
    /// from the driver's eye, live tiles (cache off), whatever code is synced:
    ///   graham   the owner's N Graham St frame (edge 20287 into node 14254, the
    ///            inside lane by the double yellow, toward Uptown) at noon, dusk, night,
    ///            plus a curb view and the right corner of the junction
    ///   uptown   The Square (Trade x Tryon) from S Tryon, noon and night
    ///   house    a rolled-curb house street (Lexington Ave, e21451): eye, curb, inlet
    ///   atando   Atando Ave (the shed street of the "types" round)
    /// The poses come from the map only (same eye on any code). With the merged
    /// code the nearest inlet / manhole (CityMeshes.InletsBuilt / LidsBuilt, by
    /// reflection) add close poses, written to Screenshots/integ/poses.txt; a run
    /// with PSX_INTEG_POSES=&lt;that file&gt; (the base code) shoots the same eyes.
    ///   -executeMethod PSXRacing.EditorTools.CityIntegShots.Run   PSX_INTEG_TAG=base|merged
    /// Output: Screenshots/integ/&lt;pose&gt;_&lt;hour&gt;_&lt;tag&gt;.png + integ_log_&lt;tag&gt;.txt
    /// </summary>
    public static class CityIntegShots
    {
        static Vector2 LL(double lat, double lon) =>
            new Vector2((float)((lon + 80.81770185962013) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                        (float)((lat - 35.18456015184093) * 111132.0));

        struct Pose { public string name, hours, ring; public Vector3 eye; public Quaternion rot; }

        public static void Run()
        {
            var I = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_INTEG_TAG") ?? "run";
            var log = new StringBuilder($"CITY INTEG SHOTS {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "integ");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            bool cacheWas = CityTileCache.Enabled;
            try
            {
                CityTileCache.Enabled = false;
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = 720;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();

                var poses = new List<Pose>();
                string posesIn = System.Environment.GetEnvironmentVariable("PSX_INTEG_POSES");
                if (!string.IsNullOrEmpty(posesIn) && File.Exists(posesIn))
                {
                    foreach (var ln in File.ReadAllLines(posesIn))
                    {
                        var f = ln.Split('\t'); if (f.Length < 10) continue;
                        float P(int i) => float.Parse(f[i], I);
                        poses.Add(new Pose { name = f[0], ring = f[1], hours = f[2], eye = new Vector3(P(3), P(4), P(5)), rot = new Quaternion(P(6), P(7), P(8), P(9)) });
                    }
                    log.Append($"{poses.Count} poses read from {posesIn}\n");
                }
                else
                {
                    // ---- graham: the owner's frame
                    var eg = map.edges[20287];
                    int node = 14254;
                    bool atB = eg.b == node;
                    float sNode = atB ? eg.length : 0f;
                    float sEye = atB ? Mathf.Max(0f, eg.length - 22f) : Mathf.Min(eg.length, 22f);
                    var t2 = eg.TangentAt(sEye); var fh = new Vector3(t2.x, 0f, t2.y).normalized; if (!atB) fh = -fh;
                    var right = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector2 p2 = eg.PointAt(sEye);
                    float lat = eg.oneway ? 0f : eg.width * 0.12f, half = eg.width * 0.5f;
                    var road = new Vector3(p2.x, eg.YAt(sEye), p2.y);
                    var eye = road + right * lat + Vector3.up * 1.1f;
                    poses.Add(new Pose { name = "graham_eye", ring = "graham", hours = "noon,dusk,night", eye = eye, rot = Quaternion.LookRotation(fh * 30f + Vector3.down * 1.1f) });
                    var curbEye = road + right * (half - 2.2f) + Vector3.up * 1.0f;
                    poses.Add(new Pose { name = "graham_curb", ring = "graham", hours = "noon", eye = curbEye, rot = Quaternion.LookRotation(road + right * half + fh * 6f - curbEye) });
                    var np = map.nodes[node];
                    var nc = new Vector3(np.x, eg.YAt(sNode), np.y);
                    var ce = nc - fh * 14f + right * (half * 0.4f) + Vector3.up * 1.5f;
                    poses.Add(new Pose { name = "graham_corner", ring = "graham", hours = "noon", eye = ce, rot = Quaternion.LookRotation(nc + right * (half + 1.5f) - fh * 4f - ce) });
                    log.Append($"graham: e20287 '{eg.name}' cls {eg.cls} width {eg.width:0.0} node {node} at {(atB ? "b" : "a")} eye s {sEye:0.0}\n");

                    // ---- uptown: The Square from S Tryon (the arm heading most nearly SW)
                    AddJunction(map, poses, log, "uptown", LL(35.22715, -80.84313), 225f, 26f, "noon,night");
                    // ---- house: a rolled-curb house street (curbs' Lexington Ave test edge)
                    {
                        var eh = map.edges[21451];
                        float s = eh.length * 0.5f;
                        var th = eh.TangentAt(s); var fhh = new Vector3(th.x, 0f, th.y).normalized;
                        var rh = Vector3.Cross(Vector3.up, fhh).normalized;
                        var ph = eh.PointAt(s); var rd = new Vector3(ph.x, eh.YAt(s), ph.y);
                        float hh = eh.width * 0.5f;
                        var he = rd + rh * (eh.oneway ? 0f : eh.width * 0.25f) + Vector3.up * 1.1f - fhh * 10f;
                        poses.Add(new Pose { name = "house_eye", ring = "house", hours = "noon", eye = he, rot = Quaternion.LookRotation(fhh * 30f + rh * 6f + Vector3.down * 1.4f) });
                        var hc = rd + rh * (hh - 2.0f) + Vector3.up * 1.2f;
                        poses.Add(new Pose { name = "house_curb", ring = "house", hours = "noon", eye = hc, rot = Quaternion.LookRotation(rd + rh * (hh + 3f) + fhh * 9f - hc) });
                        log.Append($"house: e21451 '{eh.name}' cls {eh.cls} width {eh.width:0.0}\n");
                    }
                    // ---- atando: the shed street
                    {
                        CityMap.Edge best = null; float bd = 1e9f; var at = LL(35.2597, -80.8287);
                        foreach (var e in map.edges)
                        {
                            if (e.name == null || !e.name.StartsWith("Atando") || e.length < 60f) continue;
                            float d = Vector2.Distance(e.PointAt(e.length * 0.5f), at);
                            if (d < bd) { bd = d; best = e; }
                        }
                        if (best != null)
                        {
                            float s = best.length * 0.5f;
                            var ta = best.TangentAt(s); var fa = new Vector3(ta.x, 0f, ta.y).normalized;
                            var ra = Vector3.Cross(Vector3.up, fa).normalized;
                            var pa = best.PointAt(s); var rd = new Vector3(pa.x, best.YAt(s), pa.y);
                            var ae = rd + ra * (best.oneway ? 0f : best.width * 0.25f) + Vector3.up * 1.1f;
                            poses.Add(new Pose { name = "atando_eye", ring = "atando", hours = "noon", eye = ae, rot = Quaternion.LookRotation(fa * 30f + ra * 12f + Vector3.down * 0.6f) });
                            log.Append($"atando: e{best.index} '{best.name}' {bd:0} m from the pin\n");
                        }
                        else log.Append("atando: no Atando edge found\n");
                    }
                }

                // ---- shoot, ring by ring
                var rings = new List<string>();
                foreach (var p in poses) if (!rings.Contains(p.ring)) rings.Add(p.ring);
                var extra = new List<Pose>();
                foreach (var ring in rings)
                {
                    var first = poses.Find(p => p.ring == ring);
                    world.DropAll();
                    ResetHardware();
                    world.EnsureRing(first.eye, 2);
                    Physics.SyncTransforms();
                    var list = poses.FindAll(p => p.ring == ring);
                    // the merged code: an inlet and a manhole near the ring's first eye (written out for the base run)
                    if (string.IsNullOrEmpty(posesIn) && (ring == "graham" || ring == "house"))
                    {
                        var c2 = new Vector2(first.eye.x, first.eye.z);
                        var fwd = first.rot * Vector3.forward; fwd.y = 0f; fwd.Normalize();
                        if (Nearest(c2, fwd, "InletsBuilt", out Vector3 ia, out Vector2 io))
                        {
                            var o3 = new Vector3(io.x, 0f, io.y); var a3 = Vector3.Cross(Vector3.up, o3);
                            Vector3 ie = ia - o3 * 2.4f - a3 * 2.2f + Vector3.up * 1.2f;
                            var pz = new Pose { name = ring + "_inlet", ring = ring, hours = "noon", eye = ie, rot = Quaternion.LookRotation(ia + o3 * 0.15f - ie) };
                            list.Add(pz); extra.Add(pz);
                            log.Append($"{ring}: inlet at ({ia.x:0.0},{ia.y:0.00},{ia.z:0.0})\n");
                        }
                        if (Nearest(c2, fwd, "LidsBuilt", out Vector3 la, out _))
                        {
                            Vector3 le = la - fwd * 3.2f + Vector3.up * 1.25f;
                            var pz = new Pose { name = ring + "_manhole", ring = ring, hours = "noon", eye = le, rot = Quaternion.LookRotation(la - le) };
                            list.Add(pz); extra.Add(pz);
                            log.Append($"{ring}: lid at ({la.x:0.0},{la.y:0.00},{la.z:0.0})\n");
                        }
                    }
                    var carAt = first.eye - (first.rot * Vector3.forward) * 70f; carAt.y = first.eye.y - 0.75f;
                    var carRot = Quaternion.LookRotation(Flat(first.rot * Vector3.forward));
                    if (car != null) car.TeleportTo(carAt, carRot); else player.transform.SetPositionAndRotation(carAt, carRot);
                    foreach (var h in new[] { "noon", "dusk", "night" })
                    {
                        if (!list.Exists(p => ("," + p.hours + ",").Contains("," + h + ","))) continue;
                        int hour = h == "noon" ? TimeOfDay.Noon : h == "dusk" ? TimeOfDay.Dusk : TimeOfDay.Night;
                        RaceHandoff.CalendarDay = 0;
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        foreach (var p in list)
                        {
                            if (!("," + p.hours + ",").Contains("," + h + ",")) continue;
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"integ/{p.name}_{h}_{tag}", p.eye, p.rot);
                            shots++;
                        }
                    }
                }
                if (string.IsNullOrEmpty(posesIn))
                {
                    var sb = new StringBuilder();
                    foreach (var p in poses) sb.Append(PoseLine(p, I));
                    foreach (var p in extra) sb.Append(PoseLine(p, I));
                    File.WriteAllText(Path.Combine(dir, "poses.txt"), sb.ToString());
                }
                world.DropAll();
                log.Append($"{shots} frames to {dir}\nINTEG SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("INTEG SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                CityTileCache.Enabled = cacheWas;
                File.WriteAllText(Path.Combine(dir, $"integ_log_{tag}.txt"), log.ToString());
                Debug.Log("[IntegShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string PoseLine(Pose p, CultureInfo I) =>
            string.Join("\t", p.name, p.ring, p.hours,
                p.eye.x.ToString("R", I), p.eye.y.ToString("R", I), p.eye.z.ToString("R", I),
                p.rot.x.ToString("R", I), p.rot.y.ToString("R", I), p.rot.z.ToString("R", I), p.rot.w.ToString("R", I)) + "\n";

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>A junction pose: the arm at <paramref name="node"/>'s nearest node
        /// whose far end lies most nearly at <paramref name="bearing"/> (deg from north),
        /// the eye <paramref name="back"/> m out along it in the right-hand lane, looking in.</summary>
        static void AddJunction(CityMap map, List<Pose> poses, StringBuilder log, string ring, Vector2 near, float bearing, float back, string hours)
        {
            int best = -1; float bd = 1e9f;
            for (int i = 0; i < map.nodes.Length; i++)
            {
                if (map.nodeEdges[i] == null || map.nodeEdges[i].Count < 4) continue;
                float d = Vector2.Distance(map.nodes[i], near);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) { log.Append($"{ring}: no node\n"); return; }
            var want = new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
            CityMap.Edge arm = null; float bdot = -2f;
            foreach (int ei in map.nodeEdges[best])
            {
                var e = map.edges[ei];
                var far = map.nodes[e.a == best ? e.b : e.a];
                float dt = Vector2.Dot((far - map.nodes[best]).normalized, want);
                if (e.length > back + 5f && dt > bdot) { bdot = dt; arm = e; }
            }
            if (arm == null) { log.Append($"{ring}: no arm\n"); return; }
            bool atB = arm.b == best;
            float s = atB ? arm.length - back : back;
            var t = arm.TangentAt(s); var fh = new Vector3(t.x, 0f, t.y).normalized; if (!atB) fh = -fh;
            var right = Vector3.Cross(Vector3.up, fh).normalized;
            var p = arm.PointAt(s);
            float lat = arm.oneway ? 0f : arm.width * 0.25f;
            var eye = new Vector3(p.x, arm.YAt(s), p.y) + right * lat + Vector3.up * 1.1f;
            poses.Add(new Pose { name = ring + "_eye", ring = ring, hours = hours, eye = eye, rot = Quaternion.LookRotation(fh * 30f + Vector3.down * 0.9f) });
            log.Append($"{ring}: node {best} ({bd:0} m from the pin), arm e{arm.index} '{arm.name}' cls {arm.cls}\n");
        }

        static void ResetHardware()
        {
            foreach (var n in new[] { "InletsBuilt", "LidsBuilt" })
            {
                var f = typeof(CityMeshes).GetField(n, BindingFlags.Public | BindingFlags.Static);
                (f?.GetValue(null) as System.Collections.IList)?.Clear();
            }
            var rh = typeof(CityMeshes).GetField("RecordHardware", BindingFlags.Public | BindingFlags.Static);
            if (rh != null && rh.FieldType == typeof(bool)) rh.SetValue(null, true);
        }

        /// <summary>The nearest recorded inlet / lid AHEAD of the eye (merged code only).</summary>
        static bool Nearest(Vector2 from, Vector3 fwd, string field, out Vector3 at, out Vector2 outw)
        {
            at = Vector3.zero; outw = Vector2.zero;
            var f = typeof(CityMeshes).GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (!(f?.GetValue(null) is System.Collections.IList l) || l.Count == 0) return false;
            float bd = 90f; bool got = false;
            foreach (var o in l)
            {
                var tt = o.GetType();
                var a = (Vector3)tt.GetField("Item1").GetValue(o);
                var d2 = new Vector2(a.x, a.z) - from;
                if (Vector2.Dot(d2, new Vector2(fwd.x, fwd.z)) < 4f) continue;
                float d = d2.magnitude;
                if (d < bd)
                {
                    bd = d; at = a; got = true;
                    var i2 = tt.GetField("Item2");
                    outw = i2 != null && i2.FieldType == typeof(Vector2) ? (Vector2)i2.GetValue(o) : Vector2.zero;
                }
            }
            return got;
        }
    }
}
