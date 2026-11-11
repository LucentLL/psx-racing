using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CURBS PICTURES, ROUND 2 (curbs2 r2, 2026-11-11): the same camera
    /// poses three ways - BEFORE (PSX_CITY_CURBS2=0), ROUND 1 (the pass on,
    /// round 2's switches off: ROLL2, CORNERPAN, LIDTONE, LIDMARKS) and ROUND
    /// 2 - the tiles built again between. Places: the owner's North Graham
    /// spot (the four-lane two-way N Graham with a double yellow, at a
    /// four-leg junction, looking toward Uptown), Lexington Ave and the next
    /// two rolled-curb house streets, Central Ave (corner, apron).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunCurbs2
    /// Output: Screenshots/curbs2/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;before|r1|r2&gt;.png, curbs2_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunCurbs2()
        {
            var log = new StringBuilder("CURBS ROUND 2 SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "curbs2");
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

                var places = new List<(string id, string what, CityMap.Edge e, float s, Vector3 fwdHint, bool atNode)>();
                // THE OWNER'S SPOT: N Graham two-way, four lanes, a double yellow (no centre turn lane),
                // a four-leg junction at its Uptown end, no deck within 60 m; the first such past 1 km out
                {
                    var cands = new List<(CityMap.Edge e, float d)>();
                    foreach (var e in map.edges)
                    {
                        if (e.name != "North Graham Street" || e.oneway || e.length < 40f || !CityMeshes.WalkEdge(map, e)) continue;
                        var pr = RoadProfiles.All[Mathf.Clamp(e.profile, 0, RoadProfiles.All.Length - 1)];
                        if (pr.lanes < 4 || pr.turnLane) continue;
                        int up = Vector2.Distance(map.nodes[e.a], map.uptown) < Vector2.Distance(map.nodes[e.b], map.uptown) ? e.a : e.b;
                        if (map.nodeEdges[up].Count != 4) continue;
                        var mid = e.PointAt(e.length * 0.5f);
                        var near = new HashSet<int>();
                        map.EdgeSegsInRect(mid - Vector2.one * 60f, mid + Vector2.one * 60f, near);
                        bool deck = false;
                        foreach (int pk in near) { var o = map.edges[pk >> 12]; if (o.bridge || o.cls >= 4) { deck = true; break; } }
                        if (deck) continue;
                        cands.Add((e, Vector2.Distance(map.nodes[up], map.uptown)));
                    }
                    cands.Sort((x, y) => x.d.CompareTo(y.d));
                    foreach (var c in cands) log.Append($"graham2 candidate e{c.e.index} {c.d:0} m from the Square, {c.e.length:0} m long\n");
                    CityMap.Edge best = null;
                    foreach (var c in cands) if (c.d > 1000f) { best = c.e; break; }
                    if (best == null && cands.Count > 0) best = cands[cands.Count - 1].e;
                    if (best != null)
                    {
                        int up = Vector2.Distance(map.nodes[best.a], map.uptown) < Vector2.Distance(map.nodes[best.b], map.uptown) ? best.a : best.b;
                        float s = up == best.b ? Mathf.Max(best.length - 32f, best.length * 0.4f) : Mathf.Min(32f, best.length * 0.6f);
                        var toUp = map.nodes[up] - best.PointAt(s);
                        places.Add(("graham2", "N Graham St, four lanes two-way, double yellow, at a four-leg junction toward Uptown (the owner's spot)", best, s, new Vector3(toUp.x, 0f, toUp.y), true));
                    }
                    else log.Append("graham2: no candidate\n");
                }
                // the rolled-curb house streets: the nearest three to the Square
                {
                    var rolled = new List<(CityMap.Edge e, float d)>();
                    foreach (var e in map.edges)
                    {
                        if (e.oneway || e.length < 70f || !CityMeshes.RolledEdge(map, e)) continue;
                        rolled.Add((e, Vector2.Distance(e.PointAt(e.length * 0.5f), map.uptown)));
                    }
                    rolled.Sort((x, y) => x.d.CompareTo(y.d));
                    var names = new HashSet<string>();
                    int n = 0;
                    foreach (var r in rolled)
                    {
                        if (names.Contains(r.e.name)) continue;
                        names.Add(r.e.name);
                        places.Add(("house" + n, $"house street '{r.e.name}' (rolled curb), {r.d:0} m from the Square", r.e, r.e.length * 0.5f, Vector3.zero, false));
                        if (++n >= 3) break;
                    }
                }
                if (Snap(map, LL(35.22019, -80.80899), "Central", out var ce, out float cs, out _))
                    places.Add(("central", "Central Ave, Plaza Midwood (curb and gutter)", ce, cs, new Vector3(Mathf.Sin(94f * Mathf.Deg2Rad), 0f, Mathf.Cos(94f * Mathf.Deg2Rad)), false));

                foreach (var pl in places)
                {
                    var t2 = pl.e.TangentAt(pl.s);
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    if (pl.fwdHint.sqrMagnitude > 0f && Vector3.Dot(fh, pl.fwdHint) < 0f) fh = -fh;
                    var rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector2 p = pl.e.PointAt(pl.s);
                    // two-way: the inside lane right of the double yellow (the owner's view); one-way: the middle
                    float lat = pl.e.oneway ? 0f : pl.e.width * (pl.id == "graham2" ? 0.12f : 0.25f);
                    float half = pl.e.width * 0.5f;
                    var pos = new Vector3(p.x, pl.e.YAt(pl.s) + 0.35f, p.y) + rightV * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    Vector3 eye = pos + Vector3.up * 0.75f + fh * 2.4f;
                    poses.Add(("eye", eye, Quaternion.LookRotation(fh * 30f + Vector3.down * 1.1f)));
                    Vector3 lineP = new Vector3(p.x, pl.e.YAt(pl.s), p.y);
                    Vector3 curbEye = lineP + rightV * (half - 2.2f) + Vector3.up * 1.0f;
                    poses.Add(("curb", curbEye, Quaternion.LookRotation(lineP + rightV * half + fh * 5f - curbEye)));
                    log.Append($"{pl.id}\t'{pl.e.name}' e{pl.e.index} cls {pl.e.cls} width {pl.e.width:0.0}{(pl.e.oneway ? " one-way" : "")} rolled {CityMeshes.RolledEdge(map, pl.e)}\tcar ({pos.x:0.0},{pos.y:0.00},{pos.z:0.0}) fwd ({fh.x:0.00},{fh.z:0.00})\t{pl.what}\n");
                    bool hw = pl.id == "graham2" || pl.id == "central" || pl.id == "house0";

                    foreach (var mode in new[] { "r2", "r1", "before" })
                    {
                        bool on = mode != "before", r2 = mode == "r2";
                        CityMeshes.Curbs2On = on;
                        CityMeshes.Roll2On = r2; CityMeshes.CornerPanOn = r2; CityMeshes.LidToneOn = r2; CityMeshes.LidMarksOn = r2; CityMeshes.PanWetOn = r2;
                        CityMeshes.RecordHardware = on;
                        CityMeshes.InletsBuilt.Clear(); CityMeshes.LidsBuilt.Clear();
                        CityMeshes.Curbs2Stats.Reset();
                        world.DropAll();
                        world.EnsureRing(pos, 1);
                        log.Append($"  {mode}: {CityMeshes.Curbs2Stats.Line()}\n");
                        if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                        if (r2 && hw)
                        {
                            // the nearest inlet and manhole AHEAD on this street's side, the nearest apron, the corner ahead (eyes kept for r1 and before)
                            var c2 = new Vector2(pos.x, pos.z);
                            int bi = -1; float bdi = 120f;
                            for (int k = 0; k < CityMeshes.InletsBuilt.Count; k++)
                            {
                                var at = CityMeshes.InletsBuilt[k].at;
                                float d = Vector2.Distance(new Vector2(at.x, at.z), c2);
                                if (d < bdi) { bdi = d; bi = k; }
                            }
                            if (bi >= 0)
                            {
                                var ib = CityMeshes.InletsBuilt[bi];
                                var o3 = new Vector3(ib.outw.x, 0f, ib.outw.y); var a3 = Vector3.Cross(Vector3.up, o3);
                                Vector3 ie = ib.at - o3 * 2.4f - a3 * 2.2f + Vector3.up * 1.2f;
                                poses.Add(("inlet", ie, Quaternion.LookRotation(ib.at + o3 * 0.15f - ie)));
                                log.Append($"  inlet {bdi:0} m away at ({ib.at.x:0.0},{ib.at.y:0.00},{ib.at.z:0.0}){(ib.rolled ? " rolled" : "")}\n");
                            }
                            int bl = -1; float bdl = 150f;
                            for (int k = 0; k < CityMeshes.LidsBuilt.Count; k++)
                            {
                                float d = Vector2.Distance(new Vector2(CityMeshes.LidsBuilt[k].at.x, CityMeshes.LidsBuilt[k].at.z), c2);
                                if (d < bdl) { bdl = d; bl = k; }
                            }
                            if (bl >= 0)
                            {
                                var lb = CityMeshes.LidsBuilt[bl].at;
                                Vector3 le = lb - fh * 3.2f + rightV * 0.8f + Vector3.up * 1.25f;
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
                                float back = Mathf.Min(16f, Vector2.Distance(np, new Vector2(pos.x, pos.z)) + 4f);
                                var ce2 = nc - fh * back + rightV * (lat * 0.5f) + Vector3.up * 1.4f;
                                poses.Add(("corner", ce2, Quaternion.LookRotation(nc + rightV * (half + 2f) - fh * 4f - ce2)));
                            }
                        }
                        foreach (var h in new[] { "noon", "rain" })
                        {
                            if (h == "rain" && pl.id != "graham2") continue;
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
                                if (h == "rain" && ps.name != "eye" && ps.name != "inlet" && ps.name != "manhole") continue;
                                if (!hw && ps.name == "eye" && pl.id.StartsWith("house") == false && pl.id != "central") continue;
                                var cp = pos - fh * 60f;
                                if (car != null) car.TeleportTo(cp, rot); else player.transform.SetPositionAndRotation(cp, rot);
                                CarLights.PushGlobals();
                                PSXScreenshotTool.ShotAs(cam, $"curbs2/{pl.id}_{ps.name}_{h}_{mode}", ps.eye, ps.r);
                                shots++;
                            }
                        }
                        RaceHandoff.WeatherOverride = -1;
                    }
                }
                CityMeshes.Curbs2On = true; CityMeshes.Roll2On = CityMeshes.CornerPanOn = CityMeshes.LidToneOn = CityMeshes.LidMarksOn = CityMeshes.PanWetOn = true;
                CityMeshes.RecordHardware = false;
                log.Append($"{shots} frames to {dir}\nCURBS ROUND 2 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("CURBS ROUND 2 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "curbs2_log.txt"), log.ToString());
                Debug.Log("[Curbs2Shots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
