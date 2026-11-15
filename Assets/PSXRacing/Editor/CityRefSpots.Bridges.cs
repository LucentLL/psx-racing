using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BRIDGES PICTURES, ROUND 2 (2026-11-15): driver-eye frames over and
    /// under a list of crossings, the same poses with PSX_CITY_BRIDGE2 off
    /// ("before") and on ("after"), the tiles built again between.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunBridges2
    /// PSX_BRSHOT_SPOTS: entries split by ';'
    ///   id:e&lt;edge&gt;[:pose,pose]   a crossing by its edge (its longest run on
    ///                            structure); poses over, deck, end, under, side
    ///                            (default over,deck,under or side)
    ///   id=x,z,h,lx,lz,lh        an explicit eye (h metres over the nearest
    ///                            road's surface there) and look-at point
    /// PSX_BRSHOT_MODES (default "after,before").
    /// Output: Screenshots/bridges2/&lt;id&gt;_&lt;pose&gt;_&lt;mode&gt;.png, bridges2_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        static CityMap.Edge BrPartner(CityMap map, CityMap.Edge e, bool atA)
        {
            int node = atA ? e.a : e.b;
            Vector2 dirE = atA ? e.TangentAt(0f) : -e.TangentAt(e.length);
            CityMap.Edge best = null; float bestDot = -0.5f;
            foreach (int oi in map.nodeEdges[node])
            {
                if (oi == e.index) continue;
                var o = map.edges[oi];
                Vector2 dirO = o.a == node ? o.TangentAt(0f) : -o.TangentAt(o.length);
                float dot = Vector2.Dot(dirE, dirO);
                if (dot < bestDot) { bestDot = dot; best = o; }
            }
            return best;
        }

        /// <summary>A point u metres along e's own arc, carried past its ends
        /// onto the road it runs on into: the driver's seat in the right-hand
        /// lane, on the drawn line model.</summary>
        static bool BrSeat(CityMap map, CityMap.Edge e, float u, out Vector3 seat, out Vector3 fwd)
        {
            seat = default; fwd = Vector3.forward;
            var ee = e; float s = u; bool f = true;
            if (u < 0f)
            {
                var o = BrPartner(map, e, true); if (o == null) { s = 0.5f; }
                else { float d = Mathf.Min(-u, o.length - 0.5f); if (o.a == e.a) { s = d; f = false; } else { s = o.length - d; f = true; } ee = o; }
            }
            else if (u > e.length)
            {
                var o = BrPartner(map, e, false); if (o == null) { s = e.length - 0.5f; }
                else { float d = Mathf.Min(u - e.length, o.length - 0.5f); if (o.a == e.b) { s = d; f = true; } else { s = o.length - d; f = false; } ee = o; }
            }
            var p = ee.PointAt(s); var t = ee.TangentAt(s);
            LineModel.Extents(ee, s, out float eM, out float eP);
            if (!f) { t = -t; float q = eM; eM = eP; eP = q; }
            var right = new Vector2(-t.y, t.x);
            float W = eM + eP;
            float lat = ee.oneway ? -eM + 0.3f * W : -eM * 0.5f;
            var w = p + right * lat;
            seat = new Vector3(w.x, ee.YAt(s), w.y);
            fwd = new Vector3(t.x, 0f, t.y);
            return true;
        }

        public static void RunBridges2()
        {
            var log = new StringBuilder("BRIDGES ROUND 2 SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "bridges2");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            var inv = CultureInfo.InvariantCulture;
            try
            {
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
                string spec = System.Environment.GetEnvironmentVariable("PSX_BRSHOT_SPOTS") ?? "";
                string modesS = System.Environment.GetEnvironmentVariable("PSX_BRSHOT_MODES");
                if (string.IsNullOrWhiteSpace(modesS)) modesS = "after,before";
                var modes = modesS.Split(',');
                var segs = new HashSet<int>();

                foreach (var item in spec.Split(';'))
                {
                    string it = item.Trim();
                    if (it.Length == 0) continue;
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    string id;
                    if (it.Contains("="))
                    {
                        id = it.Substring(0, it.IndexOf('='));
                        var v = it.Substring(it.IndexOf('=') + 1).Split(',');
                        float x = float.Parse(v[0], inv), z = float.Parse(v[1], inv), h = float.Parse(v[2], inv);
                        float lx = float.Parse(v[3], inv), lz = float.Parse(v[4], inv), lh = float.Parse(v[5], inv);
                        float y0 = 0f, y1 = 0f;
                        if (map.NearestRoadPoint(new Vector2(x, z), 300f, false, out int ei, out float s2, out _)) y0 = map.edges[ei].YAt(s2);
                        if (map.NearestRoadPoint(new Vector2(lx, lz), 300f, false, out int ej, out float s3, out _)) y1 = map.edges[ej].YAt(s3); else y1 = y0;
                        var eye = new Vector3(x, y0 + h, z); var look = new Vector3(lx, y1 + lh, lz);
                        poses.Add(("view", eye, Quaternion.LookRotation(look - eye)));
                        log.Append($"{id}\texplicit eye ({eye.x:0.0},{eye.y:0.00},{eye.z:0.0}) look ({look.x:0.0},{look.y:0.00},{look.z:0.0})\n");
                    }
                    else
                    {
                        var parts = it.Split(':');
                        id = parts[0];
                        if (parts.Length < 2 || !parts[1].StartsWith("e") || !int.TryParse(parts[1].Substring(1), out int ei) || ei < 0 || ei >= map.edges.Length) { log.Append($"{id}: bad spot '{it}'\n"); continue; }
                        var e = map.edges[ei];
                        // the longest run on structure
                        float s0 = -1f, s1 = -1f, from = -1f, last = 0f;
                        for (float s = 0f; ; s += 0.5f)
                        {
                            float ss = Mathf.Min(s, e.length);
                            bool el = e.ElevatedAt(ss);
                            if (el) { if (from < 0f) from = ss; last = ss; }
                            if ((!el || ss >= e.length) && from >= 0f) { if (last - from > s1 - s0) { s0 = from; s1 = last; } from = -1f; }
                            if (ss >= e.length) break;
                        }
                        if (s0 < 0f) { s0 = e.length * 0.3f; s1 = e.length * 0.7f; log.Append($"{id}: e{ei} has no run on structure; its middle is used\n"); }
                        // the road below: the highest class under the run
                        CityMap.Edge under = null; float underAt = 0f;
                        for (float s = s0; s <= s1; s += 3f)
                        {
                            var p = e.PointAt(s); float y = e.YAt(s);
                            segs.Clear();
                            map.EdgeSegsInRect(p - Vector2.one * 40f, p + Vector2.one * 40f, segs);
                            foreach (int packed in segs)
                            {
                                int oi = packed >> 12, si = packed & 0xFFF;
                                var o = map.edges[oi];
                                if (o == e || si + 1 >= o.pts.Length || o.tunnel) continue;
                                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                                float L2 = d.sqrMagnitude;
                                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                                if (Vector2.Distance(p, a + d * t) > 2.5f) continue;
                                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                                if (o.YAt(at) > y - 2.5f) continue;
                                if (under == null || o.cls > under.cls) { under = o; underAt = at; }
                            }
                        }
                        string want = parts.Length > 2 ? parts[2] : under != null ? "over,deck,under" : "over,deck,side";
                        foreach (var pn in want.Split(','))
                        {
                            Vector3 seat, fwd, seat2, f2;
                            switch (pn.Trim())
                            {
                                case "over":
                                    BrSeat(map, e, s0 - 30f, out seat, out fwd); BrSeat(map, e, s0 + 20f, out seat2, out f2);
                                    poses.Add(("over", seat + Vector3.up * 1.15f, Quaternion.LookRotation(seat2 + Vector3.up * 0.7f - (seat + Vector3.up * 1.15f))));
                                    break;
                                case "deck":
                                    BrSeat(map, e, s0 + 0.25f * (s1 - s0), out seat, out fwd); BrSeat(map, e, s0 + 0.25f * (s1 - s0) + 40f, out seat2, out f2);
                                    poses.Add(("deck", seat + Vector3.up * 1.15f, Quaternion.LookRotation(seat2 + Vector3.up * 0.7f - (seat + Vector3.up * 1.15f))));
                                    break;
                                case "end":
                                    BrSeat(map, e, s1 - 28f, out seat, out fwd); BrSeat(map, e, s1 + 20f, out seat2, out f2);
                                    poses.Add(("end", seat + Vector3.up * 1.15f, Quaternion.LookRotation(seat2 + Vector3.up * 0.7f - (seat + Vector3.up * 1.15f))));
                                    break;
                                case "under":
                                    if (under == null) break;
                                    {
                                        bool fwdOk = under.oneway || underAt > 40f;
                                        float uEye = fwdOk ? underAt - 36f : underAt + 36f;
                                        BrSeat(map, under, Mathf.Clamp(uEye, 1f, under.length - 1f), out seat, out fwd);
                                        var cp = under.PointAt(underAt);
                                        var tgt = new Vector3(cp.x, under.YAt(underAt) + 2.6f, cp.y);
                                        if (!fwdOk)
                                        {
                                            // two-way, looking back along the arc: the seat on the other half
                                            var tb = under.TangentAt(Mathf.Clamp(uEye, 1f, under.length - 1f));
                                            LineModel.Extents(under, Mathf.Clamp(uEye, 1f, under.length - 1f), out _, out float eP2);
                                            var pb = under.PointAt(Mathf.Clamp(uEye, 1f, under.length - 1f)) + new Vector2(-tb.y, tb.x) * (eP2 * 0.5f);
                                            seat = new Vector3(pb.x, under.YAt(Mathf.Clamp(uEye, 1f, under.length - 1f)), pb.y);
                                        }
                                        poses.Add(("under", seat + Vector3.up * 1.15f, Quaternion.LookRotation(tgt - (seat + Vector3.up * 1.15f))));
                                    }
                                    break;
                                case "side":
                                    {
                                        float sm = 0.5f * (s0 + s1);
                                        var p = e.PointAt(sm); var t = e.TangentAt(sm);
                                        LineModel.Extents(e, sm, out _, out float eP3);
                                        var q = p + new Vector2(-t.y, t.x) * (eP3 + 22f) - t * Mathf.Min(14f, 0.5f * (s1 - s0) + 6f);
                                        float gy = Mathf.Min(CityElevation.GroundY(map, q.x, q.y), e.YAt(sm) - 0.5f);
                                        var eye = new Vector3(q.x, gy + 1.7f, q.y);
                                        poses.Add(("side", eye, Quaternion.LookRotation(new Vector3(p.x, e.YAt(sm) - 0.8f, p.y) - eye)));
                                    }
                                    break;
                            }
                        }
                        log.Append($"{id}\te{e.index} '{e.name}' cls {e.cls}{(e.link ? " link" : "")}{(e.oneway ? " one-way" : "")} width {e.width:0.0} run {s0:0}-{s1:0} of {e.length:0}; below {(under != null ? $"e{under.index} '{under.name}' cls {under.cls}" : "no road")}; {poses.Count} poses\n");
                    }
                    if (poses.Count == 0) continue;
                    foreach (var mode in modes)
                    {
                        CityMeshes.Bridge2On = mode.Trim() != "before";
                        CityMeshes.Bridge2Changed();
                        world.DropAll();
                        RaceHandoff.CalendarDay = 0;
                        RaceHandoff.WeatherOverride = -1;
                        int hour = HourOf("noon");
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        foreach (var ps in poses)
                        {
                            world.EnsureRing(ps.eye, 1);
                            PSXTexDecode.StampAll();
                            var hide = ps.eye + Vector3.down * 60f;
                            if (car != null) car.TeleportTo(hide, ps.r); else player.transform.SetPositionAndRotation(hide, ps.r);
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"bridges2/{id}_{ps.name}_{mode.Trim()}", ps.eye, ps.r);
                            shots++;
                        }
                    }
                }
                CityMeshes.Bridge2On = true; CityMeshes.Bridge2Changed();
                log.Append($"{shots} frames to {dir}\nBRIDGES ROUND 2 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("BRIDGES ROUND 2 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "bridges2_log.txt"), log.ToString());
                Debug.Log("[Bridges2Shots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
