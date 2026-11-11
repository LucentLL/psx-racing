using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TOWER LIGHTS' PROBE (owner, 2026-11-11: "aviation red lights on
    /// buildings are outdated and not actually connected to buildings").
    /// For PSX_CITY_BEACONS2=0 (before) and 1 (after), each in its own world:
    ///   MEASURE: every light's distance to the nearest DRAWN surface - the far
    ///     skyline's triangles of its own building, and the near tiles' meshes
    ///     (every tile mesh within 30 m, the tower's tiles stood up) - and, for
    ///     V2, the gap between the point its fixture stands on and those
    ///     surfaces. A distribution and the worst twenty per mode.
    ///   SHOTS: the owner's N Graham St pose (sunset, night), three tower tops
    ///     close (noon, night) and the skyline from 5.5 km (night), each before
    ///     and after: Screenshots/beacons/&lt;pose&gt;_&lt;hour&gt;_&lt;mode&gt;.png.
    ///   -executeMethod PSXRacing.EditorTools.BeaconProbe.Run
    ///   PSX_BEACON_MODES=before,after  PSX_BEACON_MEASURE=0  PSX_BEACON_SHOTS=0
    /// Log: Screenshots/beacons/beacons_log.txt
    /// </summary>
    public static class BeaconProbe
    {
        const int DispW = 2000, DispH = 1125;

        public static void Run()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "beacons");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder("BEACON PROBE " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            int code = 0;
            string was = System.Environment.GetEnvironmentVariable("PSX_CITY_BEACONS2");
            var keepPixels = PSXQuality.Current;
            int oldWeather = RaceHandoff.WeatherOverride;
            try
            {
                var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
                if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte.unity did not open");
                var map = CityMap.Get();
                if (map == null) throw new System.Exception("no city data");
                PSXRacingBuilder.EnsureCityTextures();
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
                if (player != null) player.SetActive(false);
                RaceHandoff.WeatherOverride = 0;
                var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                var globals = Object.FindFirstObjectByType<PSXGlobals>();
                bool measure = System.Environment.GetEnvironmentVariable("PSX_BEACON_MEASURE") != "0";
                bool shots = System.Environment.GetEnvironmentVariable("PSX_BEACON_SHOTS") != "0";
                string modesS = System.Environment.GetEnvironmentVariable("PSX_BEACON_MODES");
                var modes = (string.IsNullOrEmpty(modesS) ? "before,after" : modesS).Split(',');
                List<(string name, Vector3 eye, Vector3 look, int[] hours)> poses = null;
                foreach (var mode in modes)
                {
                    System.Environment.SetEnvironmentVariable("PSX_CITY_BEACONS2", mode == "before" ? "0" : "1");
                    var go = new GameObject("~beaconWorld");
                    var world = go.AddComponent<CityWorld>();
                    try
                    {
                        world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 0);
                        var sk = world.Skyline;
                        var cb = sk != null ? sk.Beacons : null;
                        if (cb == null) throw new System.Exception("no skyline beacons");
                        log.Append($"\n=== {mode.ToUpperInvariant()} (PSX_CITY_BEACONS2={(mode == "before" ? "0" : "1")}): {cb.Lights.Count} lights, {cb.BeaconCount} top, {cb.CornerCount} corner, V2 {cb.IsV2}, towers {cb.TowerCount}, flashing {cb.FlashCount}, fixture tris {cb.FixtureTriangles}\n");
                        if (poses == null) poses = Poses(map, cb, log);
                        if (measure) Measure(map, world, sk, cb, mode, log);
                        if (shots)
                        {
                            world.DropAll();
                            foreach (var p in poses) Shoot(world, cam, sun, globals, dir, p.name, p.eye, p.look, p.hours, mode, log);
                        }
                    }
                    finally
                    {
                        world.DropAll();
                        Object.DestroyImmediate(go);
                    }
                }
                log.Append("BEACON PROBE OK\n");
            }
            catch (System.Exception ex) { log.Append("BEACON PROBE FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_CITY_BEACONS2", was);
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                File.WriteAllText(Path.Combine(dir, "beacons_log.txt"), log.ToString());
                Debug.Log("[BeaconProbe]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ---- the poses ------------------------------------------------------

        static List<(string, Vector3, Vector3, int[])> Poses(CityMap map, CityBeacons cb, StringBuilder log)
        {
            var list = new List<(string, Vector3, Vector3, int[])>();
            var fs = map.footprints;
            // the owner's frame: N Graham St about 1.1 km out, looking down it toward uptown
            {
                float best = float.MaxValue; CityMap.Edge be = null; float bs = 0f;
                foreach (var e in map.edges)
                {
                    if (e == null || e.link || e.name == null || e.name.IndexOf("Graham", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    for (float s = 0f; s <= e.length; s += 5f)
                    {
                        var p = e.PointAt(s);
                        if (p.y < map.uptown.y) continue;   // north of uptown
                        float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - 1150f);
                        if (d < best) { best = d; be = e; bs = s; }
                    }
                }
                if (be != null)
                {
                    var p = be.PointAt(bs);
                    var t = be.TangentAt(bs).normalized;
                    if (Vector2.Dot(t, map.uptown - p) < 0f) t = -t;
                    var right = new Vector2(t.y, -t.x);
                    var at = p + right * (be.oneway ? 0f : be.width * 0.25f);
                    var eye = new Vector3(at.x, be.YAt(bs) + 1.15f, at.y);
                    list.Add(("graham", eye, eye + new Vector3(t.x, 0.045f, t.y) * 100f, new[] { TimeOfDay.Sunset, TimeOfDay.Night }));
                    log.Append($"pose graham: '{be.name}' {Vector2.Distance(p, map.uptown):0} m from uptown, eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) {LatLon(eye)}\n");
                }
                else log.Append("pose graham: no Graham St found\n");
            }
            // the tower tops: the teal pyramid (C4), the tallest, a spired crown (C1; or C3 if C1 is the tallest)
            var tops = new Dictionary<int, (Vector3 top, float h)>();
            foreach (var l in cb.Lights)
                if (l.top && (!tops.TryGetValue(l.building, out var t) || l.anchor.y > t.top.y)) tops[l.building] = (l.anchor, l.towerH);
            int tallest = -1; float th = 0f;
            foreach (var kv in tops) if (kv.Value.h > th) { th = kv.Value.h; tallest = kv.Key; }
            int Hero(byte lm) { foreach (var kv in tops) if (fs[kv.Key].landmark == lm) return kv.Key; return -1; }
            int pyr = Hero(5), spire = Hero(1);
            if (spire == tallest) spire = Hero(3);
            foreach (var (name, key) in new[] { ("top_pyramid", pyr), ("top_tallest", tallest), ("top_crown", spire) })
            {
                if (key < 0 || !tops.ContainsKey(key)) { log.Append($"pose {name}: none\n"); continue; }
                var top = tops[key].top; float h = tops[key].h;
                var outw = new Vector2(top.x, top.z) - map.uptown;
                if (outw.sqrMagnitude < 100f) outw = new Vector2(0.6f, -0.8f);
                outw = (outw.normalized + new Vector2(0.35f, -0.25f)).normalized;
                float dist = 120f + 0.15f * h;
                var eye = new Vector3(top.x + outw.x * dist, top.y - 0.10f * h, top.z + outw.y * dist);
                var look = new Vector3(top.x, top.y - 0.12f * h, top.z);
                list.Add((name, eye, look, new[] { TimeOfDay.Noon, TimeOfDay.Night }));
                log.Append($"pose {name}: building #{key} ({Describe(map, key)}), top ({top.x:0.0},{top.y:0.0},{top.z:0.0}), tower {h:0} m, eye {dist:0} m off\n");
            }
            // the skyline from 5.5 km, 30 m over the ground, from the south-west
            {
                float brg = 215f * Mathf.Deg2Rad;
                float ex = map.uptown.x + Mathf.Sin(brg) * 5500f, ez = map.uptown.y + Mathf.Cos(brg) * 5500f;
                var eye = new Vector3(ex, CityElevation.GroundY(map, ex, ez) + 30f, ez);
                var look = new Vector3(map.uptown.x, CityElevation.GroundY(map, map.uptown.x, map.uptown.y) + 110f, map.uptown.y);
                list.Add(("far55", eye, look, new[] { TimeOfDay.Night }));
                log.Append($"pose far55: eye ({eye.x:0},{eye.y:0},{eye.z:0}) 5.5 km SW\n");
            }
            return list;
        }

        static void Shoot(CityWorld world, Camera cam, Light sun, PSXGlobals globals, string dir, string name, Vector3 eye, Vector3 look,
                          int[] hours, string mode, StringBuilder log)
        {
            PSXQuality.Current = PSXPixels.HD;
            int lines = PSXQuality.HDLines(DispH);
            int w = CityFarShots.Even(Mathf.RoundToInt(lines * DispW / (float)DispH));
            world.EnsureRing(eye, 2);
            var rot = Quaternion.LookRotation(look - eye);
            foreach (int hour in hours)
            {
                TimeOfDay.Apply(hour, sun);
                NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                if (globals != null) globals.Apply();
                cam.transform.SetPositionAndRotation(eye, rot);
                // twice: the tile fade and the skyline hand-over settle on the second
                world.RefreshSkyline(cam);
                CityTileFade.Tick();
                world.RefreshSkyline(cam);
                var rt = new RenderTexture(w, lines, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                rt.Create();
                StreetLights.Push(cam.transform.position, cam.transform.forward);
                PSXTexDecode.StampAll();
                // the beacons flash: hold the frame until every flashing one is
                // past the middle of its lit half is not possible per tower - the
                // shot is taken as the clock stands (about half of them lit)
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                var shown = PSXScreenshotTool.Dithered(rt);
                var tex = CityFarShots.Read(shown);
                string hn = TimeOfDay.All[hour].name.ToLowerInvariant();
                File.WriteAllBytes(Path.Combine(dir, $"{name}_{hn}_{mode}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                rt.Release(); Object.DestroyImmediate(rt);
                var cb = world.Skyline != null ? world.Skyline.Beacons : null;
                log.Append($"shot {name}_{hn}_{mode}: {w}x{lines}, far {cam.farClipPlane:0} m, tiles {world.LiveTiles}, skyline {(world.Skyline != null ? world.Skyline.Shown + "/" + world.Skyline.ElementCount : "-")}, lights lit {(cb != null && cb.LitNow())}, fixtures shown {(cb != null ? cb.FixturesShown : 0)} towers\n");
            }
            world.DropAll();
        }

        // ---- the measure ----------------------------------------------------

        struct Tri { public Vector3 a, b, c; }

        static void Measure(CityMap map, CityWorld world, CitySkyline sk, CityBeacons cb, string mode, StringBuilder log)
        {
            var fs = map.footprints;
            var sm = sk.MeshData; var elems = sk.Elements;
            var verts = sm.mesh.vertices;
            // the far skyline's triangles per building
            var farOf = new Dictionary<int, List<Tri>>();
            for (int e = 0; e < elems.Count; e++)
            {
                var f = fs[elems[e]];
                int key = f.part && f.outline >= 0 ? f.outline : elems[e];
                if (!farOf.TryGetValue(key, out var list)) { list = new List<Tri>(); farOf[key] = list; }
                for (int s = 0; s < sm.indices.Length; s++)
                {
                    int a = sm.ranges[s][e * 2], n = sm.ranges[s][e * 2 + 1];
                    var idx = sm.indices[s];
                    for (int k = 0; k + 2 < n; k += 3)
                        list.Add(new Tri { a = verts[idx[a + k]], b = verts[idx[a + k + 1]], c = verts[idx[a + k + 2]] });
                }
            }
            // the near tiles: every building's tiles stood up (ring 1 round each
            // part's centre), then every tile mesh near each light
            var stood = new HashSet<long>();
            var buildingsLit = new HashSet<int>();
            foreach (var l in cb.Lights) buildingsLit.Add(l.building);
            foreach (int b in buildingsLit)
            {
                var pts = new List<Vector2> { fs[b].centre };
                for (int i = 0; i < fs.Length; i++) if (fs[i].part && fs[i].outline == b) pts.Add(fs[i].centre);
                foreach (var p in pts)
                {
                    long k = CityWorld.TileKeyAt(p);
                    if (stood.Add(k)) world.EnsureRing(new Vector3(p.x, 0f, p.y), 0);
                }
            }
            var skip = new List<Transform>();
            if (world.Skyline != null) skip.Add(world.Skyline.transform);
            if (world.Far != null) skip.Add(world.Far.transform);
            if (world.Proxies != null) skip.Add(world.Proxies.transform);
            var near = new List<(Bounds b, Vector3[] v, int[] t, string name)>();
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                bool sk2 = false;
                foreach (var t in skip) if (mf.transform.IsChildOf(t)) { sk2 = true; break; }
                if (sk2) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                var m = mf.sharedMesh;
                if (!m.isReadable) continue;
                var vv = m.vertices; var l2w = mf.transform.localToWorldMatrix;
                for (int i = 0; i < vv.Length; i++) vv[i] = l2w.MultiplyPoint3x4(vv[i]);
                near.Add((r.bounds, vv, m.triangles, mf.name));
            }
            log.Append($"measure: {stood.Count} tiles stood for {buildingsLit.Count} lit buildings, {near.Count} near meshes\n");

            var rows = new List<(CityBeacons.Light l, float farL, float farA, float nearL, float nearA)>();
            foreach (var l in cb.Lights)
            {
                float farL = float.PositiveInfinity, farA = float.PositiveInfinity;
                if (farOf.TryGetValue(l.building, out var tl))
                    foreach (var t in tl)
                    {
                        farL = Mathf.Min(farL, Dist(l.pos, t.a, t.b, t.c));
                        farA = Mathf.Min(farA, Dist(l.anchor, t.a, t.b, t.c));
                    }
                float nearL = float.PositiveInfinity, nearA = float.PositiveInfinity;
                foreach (var nm in near)
                {
                    var bb = nm.b; bb.Expand(60f);
                    if (!bb.Contains(l.pos)) continue;
                    var v = nm.v; var tr = nm.t;
                    for (int k = 0; k + 2 < tr.Length; k += 3)
                    {
                        Vector3 a = v[tr[k]], b = v[tr[k + 1]], c = v[tr[k + 2]];
                        // a cheap reject: the triangle's box 30 m off
                        float mnx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), mxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                        if (l.pos.x < mnx - 30f || l.pos.x > mxx + 30f) continue;
                        float mnz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), mxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                        if (l.pos.z < mnz - 30f || l.pos.z > mxz + 30f) continue;
                        nearL = Mathf.Min(nearL, Dist(l.pos, a, b, c));
                        nearA = Mathf.Min(nearA, Dist(l.anchor, a, b, c));
                    }
                }
                rows.Add((l, farL, farA, nearL, nearA));
            }
            bool v2 = cb.IsV2;
            string what = v2 ? "fixture foot (the drawn point the stub stands on)" : "light (no fixture)";
            log.Append($"[{mode}] distance from each {what} to the nearest drawn surface, metres:\n");
            Dist(log, "far skyline (own building)", rows.ConvertAll(r => v2 ? r.farA : r.farL));
            Dist(log, "near tiles (any tile mesh)", rows.ConvertAll(r => v2 ? r.nearA : r.nearL));
            if (v2)
            {
                log.Append($"[{mode}] the light itself (inside its lens, {0.45f + 0.22f:0.00}-{0.45f + 0.32f:0.00} m over the foot) to the building:\n");
                Dist(log, "far skyline", rows.ConvertAll(r => r.farL));
                Dist(log, "near tiles", rows.ConvertAll(r => r.nearL));
            }
            rows.Sort((x, y) => Mathf.Max(v2 ? y.nearA : y.nearL, v2 ? y.farA : y.farL).CompareTo(Mathf.Max(v2 ? x.nearA : x.nearL, v2 ? x.farA : x.farL)));
            log.Append($"[{mode}] the worst twenty (by the larger of near and far):\n");
            for (int i = 0; i < Mathf.Min(20, rows.Count); i++)
            {
                var r = rows[i];
                log.Append($"  #{r.l.building} {Describe(map, r.l.building)} {(r.l.top ? (r.l.flash ? "FLASH top" : "steady top") : "corner")} tower {r.l.towerH:0} m at ({r.l.pos.x:0.0},{r.l.pos.y:0.0},{r.l.pos.z:0.0}) {LatLon(r.l.pos)}: " +
                           $"far {(v2 ? r.farA : r.farL):0.00} near {(v2 ? r.nearA : r.nearL):0.00}{(v2 ? $" (light far {r.farL:0.00} near {r.nearL:0.00})" : "")}\n");
            }
            world.DropAll();
        }

        static void Dist(StringBuilder log, string label, List<float> d)
        {
            var s = d.FindAll(x => !float.IsInfinity(x));
            int inf = d.Count - s.Count;
            s.Sort();
            if (s.Count == 0) { log.Append($"  {label}: none measured ({inf} with no surface within 30 m)\n"); return; }
            float P(float q) => s[Mathf.Clamp(Mathf.RoundToInt(q * (s.Count - 1)), 0, s.Count - 1)];
            int under5 = s.FindAll(x => x <= 0.05f).Count, under50 = s.FindAll(x => x <= 0.5f).Count, over2 = s.FindAll(x => x > 2f).Count;
            log.Append($"  {label}: n {s.Count} (+{inf} with nothing drawn within 30 m), min {s[0]:0.000} p50 {P(0.5f):0.000} p90 {P(0.9f):0.000} max {s[s.Count - 1]:0.000}; <=5 cm {under5}, <=0.5 m {under50}, >2 m {over2}\n");
        }

        static string Describe(CityMap map, int key)
        {
            var f = map.footprints[key];
            string hero = f.landmark == 1 ? "C1 spired crown" : f.landmark == 2 ? "C2 open frame" : f.landmark == 3 ? "C3 silver crown" : f.landmark == 5 ? "C4 teal pyramid" : f.landmark != 0 ? "landmark " + f.landmark : "";
            return (hero.Length > 0 ? hero + ", " : "") + $"roof {f.roof}, {f.h:0} m";
        }

        static string LatLon(Vector3 p)
        {
            double lat = p.z / 111132.0 + 35.18456015184093;
            double lon = p.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)) - 80.81770185962013;
            return $"{lat:0.00000},{lon:0.00000}";
        }

        /// <summary>A point's distance to a triangle (Ericson, closest point).</summary>
        static float Dist(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return (p - a).magnitude;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return (p - b).magnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return (p - (a + v * ab)).magnitude; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return (p - c).magnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return (p - (a + w * ac)).magnitude; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return (p - (b + w * (c - b))).magnitude; }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            return (p - (a + ab * vv + ac * ww)).magnitude;
        }
    }
}
