using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CURBS 3 PICTURES (2026-11-15): the same camera poses BEFORE
    /// (PSX_CITY_INLET2=0, PSX_CITY_CURBS3=0) and AFTER, the tiles built
    /// again between. PSX_CURBS3_SET=inlet: the two inlets only (the owner's
    /// kind: the rolled-curb inlet nearest West Trade St / Beatties Ford Rd,
    /// and the vertical-curb inlet nearest it). Else also a subdivision
    /// street, a pre-war street, an arterial and a rural road.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunCurbs3
    /// Output: Screenshots/curbs3/&lt;place&gt;_&lt;pose&gt;_&lt;before|after&gt;.png, curbs3_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        static void Curbs3Mode(bool after)
        {
            CityMeshes.Inlet2On = after;
            CityMeshes.Curbs3On = after;
            CityMeshes.PanToneOn = after;
            CityMeshes.Curbs3Reset();
        }

        public static void RunCurbs3()
        {
            var log = new StringBuilder("CURBS 3 SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "curbs3");
            Directory.CreateDirectory(dir);
            string set = System.Environment.GetEnvironmentVariable("PSX_CURBS3_SET") ?? "all";
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
                RaceHandoff.CalendarDay = 0; RaceHandoff.WeatherOverride = -1;
                int hour = HourOf("noon");

                // ---- the places: an edge and an arc on it ----
                var places = new List<(string id, string what, CityMap.Edge e, float s, bool inlets)>();
                Vector2 five = LL(35.2412, -80.8553);   // West Trade St at Beatties Ford Rd (Five Points)
                Curbs3Mode(false);
                world.EnsureRing(new Vector3(five.x, 0f, five.y), 0);   // (a tile build binds the generated buildings the rule reads)
                {
                    CityMap.Edge best = null; float bd = float.MaxValue;
                    foreach (var e in map.edges)
                    {
                        if (e.oneway || e.length < 90f || !CityMeshes.RolledEdge(map, e)) continue;
                        float d = Vector2.Distance(e.PointAt(e.length * 0.5f), five);
                        if (d < bd) { bd = d; best = e; }
                    }
                    if (best != null) places.Add(("rolled", $"'{best.name}' (rolled curb), {bd:0} m from West Trade St at Beatties Ford Rd", best, best.length * 0.5f, true));
                    CityMap.Edge vb = null; float vd = float.MaxValue;
                    foreach (var e in map.edges)
                    {
                        if (e.length < 90f || e.cls < 1 || !CityMeshes.WalkEdge(map, e) || CityMeshes.RolledEdge(map, e)) continue;
                        if (e.name != "West Trade Street" && e.name != "Beatties Ford Road") continue;
                        float d = Vector2.Distance(e.PointAt(e.length * 0.5f), five);
                        if (d < vd) { vd = d; vb = e; }
                    }
                    if (vb != null) places.Add(("vertical", $"'{vb.name}' (vertical curb), {vd:0} m from Five Points", vb, vb.length * 0.5f, true));
                }
                if (set != "inlet") Curbs3Places(map, places, log);

                foreach (var pl in places)
                {
                    var t2 = pl.e.TangentAt(pl.s);
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    var rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector2 p = pl.e.PointAt(pl.s);
                    float half = pl.e.width * 0.5f;
                    float lat = pl.e.oneway ? 0f : pl.e.width * 0.25f;
                    var pos = new Vector3(p.x, pl.e.YAt(pl.s) + 0.35f, p.y) + rightV * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    Vector3 lineP = new Vector3(p.x, pl.e.YAt(pl.s), p.y);
                    if (!pl.inlets)
                    {
                        poses.Add(("eye", pos + Vector3.up * 0.75f, Quaternion.LookRotation(fh * 30f + Vector3.down * 1.6f + rightV * 3f)));
                        Vector3 curbEye = lineP + rightV * (half - 2.4f) - fh * 3f + Vector3.up * 1.15f;
                        poses.Add(("curb", curbEye, Quaternion.LookRotation(lineP + rightV * (half + 0.3f) + fh * 5f - curbEye)));
                    }
                    log.Append($"{pl.id}\t{pl.what}\te{pl.e.index} cls {pl.e.cls} width {pl.e.width:0.0}{(pl.e.oneway ? " one-way" : "")}\tat ({pos.x:0.0},{pos.y:0.00},{pos.z:0.0})\n");

                    // the inlets of both builds near the place, for the poses (both builds shot from all of them)
                    foreach (bool after in new[] { false, true })
                    {
                        Curbs3Mode(after);
                        CityMeshes.RecordHardware = true;
                        CityMeshes.InletsBuilt.Clear(); CityMeshes.Curbs2Stats.Reset(); CityMeshes.Curbs3Stats.Reset();
                        world.DropAll();
                        world.EnsureRing(pos, 1);
                        string m = after ? "after" : "before";
                        log.Append($"  {m}: {CityMeshes.Curbs2Stats.Line()}\n  {m}: {CityMeshes.Curbs3Stats.Line()}\n");
                        if (pl.inlets || (after && pl.id == "subdiv"))
                        {
                            bool wantRolled = pl.id == "rolled";
                            int bi = -1; float bdi = 200f;
                            for (int k = 0; k < CityMeshes.InletsBuilt.Count; k++)
                            {
                                var ib0 = CityMeshes.InletsBuilt[k];
                                // the place's own street's inlets: on its line, the wanted kind
                                var q = new Vector2(ib0.at.x, ib0.at.z);
                                float dLine = Mathf.Abs(Vector2.Dot(q - p, new Vector2(rightV.x, rightV.z)));
                                if (dLine > half + 1.5f) continue;
                                if (!after && ib0.rolled != wantRolled) continue;
                                float d = Vector2.Distance(q, p);
                                if (d < bdi) { bdi = d; bi = k; }
                            }
                            if (bi >= 0)
                            {
                                var ib = CityMeshes.InletsBuilt[bi];
                                var o3 = new Vector3(ib.outw.x, 0f, ib.outw.y); var a3 = Vector3.Cross(Vector3.up, o3);
                                Vector3 ie = ib.at - o3 * 2.6f - a3 * 2.6f + Vector3.up * 1.05f;
                                poses.Add(("inlet_" + m, ie, Quaternion.LookRotation(ib.at + o3 * 0.3f - ie)));
                                Vector3 ae = ib.at - o3 * 1.5f - a3 * 8.5f + Vector3.up * 1.1f;
                                poses.Add(("along_" + m, ae, Quaternion.LookRotation(ib.at + o3 * 0.2f + a3 * 2f - ae)));
                                log.Append($"  {m}: inlet {bdi:0} m from the place at ({ib.at.x:0.0},{ib.at.y:0.00},{ib.at.z:0.0}){(ib.rolled ? " rolled" : "")}\n");
                            }
                            else log.Append($"  {m}: no inlet of the kind on the place's street in the ring\n");
                        }
                        if (after && (pl.id == "subdiv" || pl.id == "prewar"))
                        {
                            // a driveway of this street: across the road from its mouth
                            var dl = new List<CityHouses.Driveway>();
                            CityHouses.DrivewaysNearBound(map, p - Vector2.one * 120f, p + Vector2.one * 120f, dl);
                            CityHouses.Driveway bd = null; float bdd = 1e9f;
                            foreach (var d in dl) { if (d.edge != pl.e.index || d.toLot) continue; float dd = Mathf.Abs(d.s - pl.s); if (dd < bdd) { bdd = dd; bd = d; } }
                            if (bd != null)
                            {
                                var f3 = new Vector3(bd.foot.x, pl.e.YAt(bd.s), bd.foot.y);
                                var n3 = new Vector3(bd.nrm.x, 0f, bd.nrm.y); var al3 = Vector3.Cross(Vector3.up, n3);
                                Vector3 de = f3 - n3 * 4.2f + al3 * 4.5f + Vector3.up * 1.25f;
                                poses.Add(("drive", de, Quaternion.LookRotation(f3 + n3 * 1.2f - de)));
                                log.Append($"  driveway at s {bd.s:0.0} ({bd.foot.x:0.0},{bd.foot.y:0.0})\n");
                            }
                            else log.Append("  no driveway on the place's street\n");
                        }
                    }
                    // shoot: after is built now; then before again
                    foreach (bool after in new[] { true, false })
                    {
                        if (!after)
                        {
                            Curbs3Mode(false);
                            world.DropAll();
                            world.EnsureRing(pos, 1);
                        }
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        PSXTexDecode.StampAll();
                        foreach (var ps in poses)
                        {
                            var cp = pos - fh * 60f;
                            if (car != null) car.TeleportTo(cp, rot); else player.transform.SetPositionAndRotation(cp, rot);
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"curbs3/{pl.id}_{ps.name}_{(after ? "after" : "before")}", ps.eye, ps.r);
                            shots++;
                        }
                    }
                }
                Curbs3Mode(true);
                CityMeshes.RecordHardware = false;
                log.Append($"{shots} frames to {dir}\nCURBS 3 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("CURBS 3 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(dir, "curbs3_log.txt"), log.ToString());
                Debug.Log("[Curbs3Shots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The curb-and-gutter places (part 2): filled in with the rule.</summary>
        static void Curbs3Places(CityMap map, List<(string id, string what, CityMap.Edge e, float s, bool inlets)> places, StringBuilder log)
        {
            // a pre-war house street near the Square (11-11: rolled): Elizabeth
            Curbs3Mode(false);
            Vector2 eliz = LL(35.2135, -80.8265);
            CityMap.Edge pw = null; float pd = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.oneway || e.length < 100f || !CityMeshes.RolledEdge(map, e)) continue;
                float d = Vector2.Distance(e.PointAt(e.length * 0.5f), eliz);
                if (d < pd) { pd = d; pw = e; }
            }
            Curbs3Mode(true);
            // a subdivision street (the roll curb alone), the arterial nearest it that had no curb, a rural road
            Vector2 sub = LL(35.2655, -80.7790);   // Hidden Valley
            CityMap.Edge se = null, ae = null, re = null; float sd = float.MaxValue, ad = float.MaxValue, rd = float.MaxValue;
            Vector2 far = LL(35.3300, -80.9300);
            foreach (var e in map.edges)
            {
                if (e.length < 150f || e.link || e.bridge || e.tunnel || e.cls > 3) continue;
                int cc = CityMeshes.CurbClassOf(map, e);
                var mid = e.PointAt(e.length * 0.5f);
                if (cc == CityMeshes.CurbRollOnly && !e.oneway)
                {
                    float d = Vector2.Distance(mid, sub);
                    if (d < sd) { sd = d; se = e; }
                }
                else if (cc == CityMeshes.CurbVertical && e.cls >= 2 && Vector2.Distance(mid, map.uptown) > 3500f && !map.AnyFootprintNear(mid, 40f, nonHouseOnly: true))
                {
                    float d = Vector2.Distance(mid, sub);
                    if (d < ad) { ad = d; ae = e; }
                }
                else if (cc == CityMeshes.CurbNone && e.cls >= 1 && e.cls <= 3 && e.width >= 6f)
                {
                    float d = Vector2.Distance(mid, far);
                    if (d < rd) { rd = d; re = e; }
                }
            }
            if (se != null) places.Add(("subdiv", $"subdivision street '{se.name}' (roll curb alone), {sd:0} m from the Hidden Valley anchor", se, se.length * 0.5f, false));
            if (pw != null) places.Add(("prewar", $"pre-war house street '{pw.name}' (Elizabeth), {pd:0} m from the anchor", pw, pw.length * 0.5f, false));
            if (ae != null) places.Add(("arterial", $"arterial '{ae.name}' through houses (class {ae.cls}), {ad:0} m from the Hidden Valley anchor", ae, ae.length * 0.5f, false));
            if (re != null) places.Add(("rural", $"rural road '{re.name}' (class {re.cls}, no curb), {rd:0} m from the north-west anchor", re, re.length * 0.5f, false));
            Curbs3Mode(false);
        }
    }
}
