using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CURBS PICTURES (curbs2, 2026-11-11): the same camera poses with the
    /// curbs pass OFF (PSX_CITY_CURBS2=0, the game as the owner drove it) and
    /// ON, the tiles built again between, at: the owner's North Graham Street
    /// spot (the N Graham walk street about a kilometre out of the Square,
    /// looking toward Uptown), S Tryon (Uptown's granite curb), the nearest
    /// rolled-curb residential street and Central Avenue in Plaza Midwood (an
    /// arterial with curb and gutter). Poses: chase, eye (the driver's), curb
    /// (a metre up, 2.2 m off the right curb), and with the pass on the
    /// nearest inlet, manhole, driveway apron and corner (the same eyes then
    /// shot with it off). N Graham also at night and in the rain.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunCurbs
    /// Output: Screenshots/curbs/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;before|after&gt;.png, curbs_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunCurbs()
        {
            var log = new StringBuilder("CURBS SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "curbs");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            int code = 0, shots = 0;
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

                var places = new List<(string id, string what, CityMap.Edge e, float s, Vector3 fwdHint)>();
                // the owner's spot: the N Graham walk street about 1 km out, looking to the Square
                {
                    CityMap.Edge best = null; float bd = float.MaxValue;
                    foreach (var e in map.edges)
                    {
                        if (e.name != "North Graham Street" || e.length < 50f || !CityMeshes.WalkEdge(map, e)) continue;
                        float d = Mathf.Abs(Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown) - 1000f);
                        if (d < bd) { bd = d; best = e; }
                    }
                    if (best != null)
                    {
                        var m = best.PointAt(best.length * 0.5f);
                        var toUp = map.uptown - m;
                        places.Add(("graham", "North Graham Street about 1 km out, toward Uptown (the owner's spot)", best, best.length * 0.5f, new Vector3(toUp.x, 0f, toUp.y)));
                    }
                    else log.Append("graham: no N Graham walk edge\n");
                }
                if (Snap(map, LL(35.22560, -80.84520), "Tryon", out var te, out float ts, out _))
                    places.Add(("tryon", "S Tryon St, 3rd to 4th (Uptown granite curb)", te, ts, new Vector3(Mathf.Sin(50f * Mathf.Deg2Rad), 0f, Mathf.Cos(50f * Mathf.Deg2Rad))));
                {
                    CityMap.Edge best = null; float bd = float.MaxValue;
                    foreach (var e in map.edges)
                    {
                        if (e.oneway || e.length < 70f || !CityMeshes.RolledEdge(map, e)) continue;
                        float d = Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown);
                        if (d < bd) { bd = d; best = e; }
                    }
                    if (best != null) places.Add(("rolled", $"residential '{best.name}' (rolled curb), {bd:0} m from the Square", best, best.length * 0.5f, Vector3.zero));
                    else log.Append("rolled: no rolled street\n");
                }
                if (Snap(map, LL(35.22019, -80.80899), "Central", out var ce, out float cs, out _))
                    places.Add(("central", "Central Ave, Plaza Midwood (arterial, curb and gutter)", ce, cs, new Vector3(Mathf.Sin(94f * Mathf.Deg2Rad), 0f, Mathf.Cos(94f * Mathf.Deg2Rad))));

                foreach (var pl in places)
                {
                    var t2 = pl.e.TangentAt(pl.s);
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    if (pl.fwdHint.sqrMagnitude > 0f && Vector3.Dot(fh, pl.fwdHint) < 0f) fh = -fh;
                    var rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector2 p = pl.e.PointAt(pl.s);
                    float lat = pl.e.oneway ? 0f : pl.e.width * 0.25f;
                    float half = pl.e.oneway ? pl.e.width * 0.5f : pl.e.width * 0.5f;
                    var pos = new Vector3(p.x, pl.e.YAt(pl.s) + 0.35f, p.y) + rightV * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    Vector3 chase = pos - fh * 8f + Vector3.up * 2.6f;
                    poses.Add(("chase", chase, Quaternion.LookRotation(pos + Vector3.up * 0.8f + fh * 20f - chase)));
                    Vector3 eye = pos + Vector3.up * 0.85f + fh * 0.3f;
                    poses.Add(("eye", eye, Quaternion.LookRotation(fh * 30f + Vector3.down * 1.1f)));
                    Vector3 lineP = new Vector3(p.x, pl.e.YAt(pl.s), p.y);
                    Vector3 curbEye = lineP + rightV * (half - 2.2f) + Vector3.up * 1.0f;
                    poses.Add(("curb", curbEye, Quaternion.LookRotation(lineP + rightV * half + fh * 5f - curbEye)));
                    log.Append($"{pl.id}\t'{pl.e.name}' e{pl.e.index} cls {pl.e.cls} width {pl.e.width:0.0}{(pl.e.oneway ? " one-way" : "")} rolled {CityMeshes.RolledEdge(map, pl.e)}\tcar ({pos.x:0.0},{pos.y:0.00},{pos.z:0.0}) fwd ({fh.x:0.00},{fh.z:0.00})\t{pl.what}\n");

                    foreach (var mode in new[] { "after", "before" })
                    {
                        bool on = mode == "after";
                        CityMeshes.Curbs2On = on;
                        CityMeshes.RecordHardware = on;
                        CityMeshes.InletsBuilt.Clear(); CityMeshes.LidsBuilt.Clear();
                        CityMeshes.Curbs2Stats.Reset();
                        world.DropAll();
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        world.EnsureRing(pos, 1);
                        log.Append($"  {mode}: ring of 9 tiles built in {clock.ElapsedMilliseconds} ms; {CityMeshes.Curbs2Stats.Line()}\n");
                        if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                        if (on)
                        {
                            // the nearest inlet, manhole, apron and corner (their eyes kept for the before)
                            var c2 = new Vector2(pos.x, pos.z);
                            int bi = -1; float bdi = 90f;
                            for (int k = 0; k < CityMeshes.InletsBuilt.Count; k++)
                            {
                                float d = Vector2.Distance(new Vector2(CityMeshes.InletsBuilt[k].at.x, CityMeshes.InletsBuilt[k].at.z), c2);
                                if (d < bdi) { bdi = d; bi = k; }
                            }
                            if (bi >= 0)
                            {
                                var ib = CityMeshes.InletsBuilt[bi];
                                var o3 = new Vector3(ib.outw.x, 0f, ib.outw.y); var a3 = Vector3.Cross(Vector3.up, o3);
                                Vector3 ie = ib.at - o3 * 2.6f - a3 * 2.4f + Vector3.up * 1.25f;
                                poses.Add(("inlet", ie, Quaternion.LookRotation(ib.at + o3 * 0.2f - ie)));
                                log.Append($"  inlet {bdi:0} m away at ({ib.at.x:0.0},{ib.at.y:0.00},{ib.at.z:0.0}){(ib.rolled ? " rolled" : "")}\n");
                            }
                            int bl = -1; float bdl = 120f;
                            for (int k = 0; k < CityMeshes.LidsBuilt.Count; k++)
                            {
                                float d = Vector2.Distance(new Vector2(CityMeshes.LidsBuilt[k].at.x, CityMeshes.LidsBuilt[k].at.z), c2);
                                if (d < bdl) { bdl = d; bl = k; }
                            }
                            if (bl >= 0)
                            {
                                var lb = CityMeshes.LidsBuilt[bl].at;
                                Vector3 le = lb - fh * 3.5f + rightV * 0.8f + Vector3.up * 1.3f;
                                poses.Add(("manhole", le, Quaternion.LookRotation(lb - le)));
                                log.Append($"  manhole {bdl:0} m away at ({lb.x:0.0},{lb.y:0.00},{lb.z:0.0})\n");
                            }
                            for (float d = 0f; d < 80f; d += 1f)
                            {
                                bool got = false;
                                foreach (float sg in new[] { 1f, -1f })
                                {
                                    var q = lineP + rightV * (half + 0.8f) + fh * (d * sg);
                                    if (!CityMeshes.ApronAtForTools(map, q.x, q.z)) continue;
                                    var ae = q - rightV * 4.5f - fh * 3f + Vector3.up * 1.4f;
                                    poses.Add(("apron", ae, Quaternion.LookRotation(q - ae)));
                                    log.Append($"  apron {d * sg:0} m along\n");
                                    got = true; break;
                                }
                                if (got) break;
                            }
                            int node = Vector2.Dot(map.nodes[pl.e.b] - pl.e.PointAt(pl.s), new Vector2(fh.x, fh.z)) > 0f ? pl.e.b : pl.e.a;
                            var np = map.nodes[node];
                            if (map.nodeEdges[node].Count >= 3)
                            {
                                var nc = new Vector3(np.x, pos.y - 0.35f, np.y);
                                var ce2 = nc - fh * 16f + rightV * (lat * 0.5f) + Vector3.up * 1.4f;
                                poses.Add(("corner", ce2, Quaternion.LookRotation(nc + rightV * (half + 2f) - fh * 4f - ce2)));
                            }
                        }
                        foreach (var h in pl.id == "graham" ? new[] { "noon", "night", "rain" } : new[] { "noon" })
                        {
                            RaceHandoff.CalendarDay = 0;
                            RaceHandoff.WeatherOverride = h == "rain" ? (int)Weather.Rain : -1;
                            int hour = h == "rain" ? TimeOfDay.Afternoon : HourOf(h);
                            TimeOfDay.Apply(hour, sun);
                            var globals = Object.FindAnyObjectByType<PSXGlobals>();
                            if (globals != null) globals.Apply();
                            NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                            PSXTexDecode.StampAll();
                            foreach (var ps in poses)
                            {
                                if (h != "noon" && ps.name != "eye" && ps.name != "chase" && ps.name != "inlet") continue;
                                CarLights.PushGlobals();
                                PSXScreenshotTool.ShotAs(cam, $"curbs/{pl.id}_{ps.name}_{h}_{mode}", ps.eye, ps.r);
                                shots++;
                            }
                        }
                        RaceHandoff.WeatherOverride = -1;
                    }
                }
                CityMeshes.Curbs2On = true; CityMeshes.RecordHardware = false;
                log.Append($"{shots} frames to {dir}\nCURBS SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("CURBS SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "curbs_log.txt"), log.ToString());
                Debug.Log("[CurbsShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
