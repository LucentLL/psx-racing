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
    /// THE CURBS 4 PICTURES ("hoods 2", 2026-11-15): ONE STATE A PROCESS - the
    /// frames of this process's own switches, labelled PSX_CURBS4_LABEL (base /
    /// after), from poses fixed by the map alone, so a base run (the switches
    /// off in the environment) and an after run shoot the same views.
    /// Places: the owner's Beatties Ford Rd pose (28.png), a pre-war house
    /// street, a subdivision street and its driveway, an arterial, a new
    /// cul-de-sac bulb (the after run picks it and writes curbs4_bulb.txt in
    /// the project root; the base run reads it). Also the ground's section
    /// across the owner's curb (heights every 5 cm from the road's middle).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunCurbs4
    /// Output: Screenshots/curbs4_&lt;label&gt;/&lt;place&gt;_&lt;pose&gt;.png, curbs4_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunCurbs4()
        {
            string label = System.Environment.GetEnvironmentVariable("PSX_CURBS4_LABEL") ?? "after";
            var log = new StringBuilder($"CURBS 4 SHOTS ({label}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; curbs3 {CityMeshes.Curbs3On}, strip {CityMeshes.StripOn}, valley {CityMeshes.ValleyOn}, std bulbs {CityMeshes.StdBulbsOn}, bulb curb {CityMeshes.BulbCurbOn}, walk clear {CityMeshes.WalkClearOn}, pan tone {CityMeshes.PanToneOn}\n");
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "curbs4_" + label);
            Directory.CreateDirectory(dir);
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

                CityMap.Edge Nearest(Vector2 at, out float s, System.Func<CityMap.Edge, bool> ok = null)
                {
                    CityMap.Edge be = null; float bd = float.MaxValue; s = 0f;
                    foreach (var e in map.edges)
                    {
                        if (e.a == e.b || e.length < 30f || e.link || (ok != null && !ok(e))) continue;
                        CityElevation.ProjectOn(e, at, out float sp);
                        sp = Mathf.Clamp(sp, 0f, e.length);
                        float d = Vector2.Distance(e.PointAt(sp), at);
                        if (d < bd) { bd = d; be = e; s = sp; }
                    }
                    return be;
                }
                var views = new List<(string place, string pose, Vector3 eye, Quaternion rot, Vector3 ring)>();
                void Street(string place, Vector2 at, bool curbView)
                {
                    var e = Nearest(at, out float s);
                    if (e == null) return;
                    var t2 = e.TangentAt(s); var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    var rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    var p = e.PointAt(s);
                    float half = e.width * 0.5f, lat = e.oneway ? 0f : e.width * 0.25f;
                    var lineP = new Vector3(p.x, e.YAt(s), p.y);
                    var pos = lineP + rightV * lat + Vector3.up * 1.1f;
                    views.Add((place, "eye", pos, Quaternion.LookRotation(fh * 30f + Vector3.down * 1.6f + rightV * 3f), lineP));
                    if (curbView)
                    {
                        Vector3 ce = lineP + rightV * (half - 2.4f) - fh * 3f + Vector3.up * 1.15f;
                        views.Add((place, "curb", ce, Quaternion.LookRotation(lineP + rightV * (half + 0.6f) + fh * 5f - ce), lineP));
                    }
                    log.Append($"{place}: e{e.index} '{e.name}' cls {e.cls} s {s:0} class {CityMeshes.CurbClassOf(map, e)} strip {CityMeshes.StripOf(map, e):0.00}\n");
                }
                // 1. the owner's pose (the lots round's frame 0_eye_after.png; his crop 28.png)
                var ownerEye = new Vector3(-3554.643555f, 127.264191f, 6935.878418f);
                var ownerRot = Quaternion.Euler(3.073677f, 31.069527f, 0f);
                views.Add(("owner", "eye", ownerEye, ownerRot, ownerEye));
                views.Add(("owner", "curb", ownerEye + Vector3.down * 0.15f, Quaternion.Euler(13f, 52f, 0f), ownerEye));
                Street("prewar", new Vector2(-1192.5f, 2900.8f), true);
                Street("subdiv", new Vector2(3392.0f, 9283.2f), true);
                Street("arterial", new Vector2(4032.0f, 8623.4f), false);
                // the subdivision street's driveway (the map's own, the same in every run)
                {
                    var at = new Vector2(3392.0f, 9283.2f);
                    world.EnsureRing(new Vector3(at.x, 0f, at.y), 0);
                    var e = Nearest(at, out float s);
                    var dl = new List<CityHouses.Driveway>();
                    CityHouses.DrivewaysNearBound(map, at - Vector2.one * 120f, at + Vector2.one * 120f, dl);
                    CityHouses.Driveway bd = null; float bdd = 1e9f;
                    foreach (var d in dl) { if (e == null || d.edge != e.index || d.toLot) continue; float dd = Mathf.Abs(d.s - s); if (dd < bdd) { bdd = dd; bd = d; } }
                    if (bd != null)
                    {
                        var f3 = new Vector3(bd.foot.x, e.YAt(bd.s), bd.foot.y);
                        var n3 = new Vector3(bd.nrm.x, 0f, bd.nrm.y); var al3 = Vector3.Cross(Vector3.up, n3);
                        Vector3 de = f3 - n3 * 4.2f + al3 * 4.5f + Vector3.up * 1.25f;
                        views.Add(("subdiv", "drive", de, Quaternion.LookRotation(f3 + n3 * 1.2f - de), f3));
                    }
                }
                // a standard bulb: the after run picks the one nearest the subdivision place, the base run reads it
                {
                    string bf = Path.Combine(root, "curbs4_bulb.txt");
                    int node = -1;
                    var trims = world.NodeTrims;
                    if (label != "base" && trims != null && trims.bulb != null)
                    {
                        var tagged = new HashSet<int>();
                        if (map.turns != null) foreach (var t in map.turns) tagged.Add(t.node);
                        float bd = float.MaxValue; var at = new Vector2(3392.0f, 9283.2f);
                        for (int n = 0; n < map.nodes.Length; n++)
                        {
                            if (!trims.bulb[n] || tagged.Contains(n)) continue;
                            float d = Vector2.Distance(map.nodes[n], at);
                            if (d < bd) { bd = d; node = n; }
                        }
                        if (node >= 0) File.WriteAllText(bf, node.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (File.Exists(bf)) int.TryParse(File.ReadAllText(bf).Trim(), out node);
                    if (node >= 0 && node < map.nodes.Length && map.nodeEdges[node].Count == 1)
                    {
                        var e = map.edges[map.nodeEdges[node][0]];
                        bool endB = e.b == node;
                        float sEnd = endB ? e.length : 0f;
                        var tn = e.TangentAt(sEnd) * (endB ? 1f : -1f);
                        var np = map.nodes[node];
                        var n3 = new Vector3(np.x, e.YAt(sEnd), np.y);
                        var f3 = new Vector3(tn.x, 0f, tn.y);
                        var rv = Vector3.Cross(Vector3.up, f3).normalized;
                        Vector3 eye = n3 - f3 * 30f + rv * 1.8f + Vector3.up * 1.1f;
                        views.Add(("bulb", "eye", eye, Quaternion.LookRotation(n3 + Vector3.up * 0.2f - eye), n3));
                        Vector3 high = n3 - f3 * 26f + Vector3.up * 16f;
                        views.Add(("bulb", "high", high, Quaternion.LookRotation(n3 - f3 * 2f - high), n3));
                        log.Append($"bulb: node {node} e{e.index} '{e.name}' at ({np.x:0.0},{np.y:0.0}), drawn as a bulb {(trims != null && trims.bulb != null && trims.bulb[node])} radius {(trims != null && trims.bulbRad != null ? trims.bulbRad[node] : 0f):0.00}\n");
                    }
                    else log.Append("bulb: none chosen\n");
                }

                Vector3 ringAt = new Vector3(float.NaN, 0f, 0f);
                foreach (var v in views)
                {
                    if (float.IsNaN(ringAt.x) || Vector3.Distance(ringAt, v.ring) > 60f)
                    {
                        CityMeshes.Curbs2Stats.Reset(); CityMeshes.Curbs3Stats.Reset(); CityMeshes.Cs4Stats.Reset();
                        world.DropAll();
                        world.EnsureRing(v.ring, 1);
                        ringAt = v.ring;
                        log.Append($"  ring at {v.place}: {CityMeshes.Curbs2Stats.Line()}\n  {CityMeshes.Cs4Stats.Line()}\n");
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        PSXTexDecode.StampAll();
                        if (v.place == "owner") Curbs4Section(map, log, ownerEye, ownerRot);
                    }
                    var cp = v.ring + new Vector3(0f, 0.5f, 0f) - (v.rot * Vector3.forward) * 80f;
                    if (car != null) car.TeleportTo(cp, v.rot); else player.transform.SetPositionAndRotation(cp, v.rot);
                    CarLights.PushGlobals();
                    PSXScreenshotTool.ShotAs(cam, $"curbs4_{label}/{v.place}_{v.pose}", v.eye, v.rot);
                    shots++;
                }
                log.Append($"{shots} frames to {dir}\nCURBS 4 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("CURBS 4 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(dir, "curbs4_log.txt"), log.ToString());
                Debug.Log("[Curbs4Shots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The ground's section across the curb the owner looks at: the street nearest his
        /// eye, 9 m ahead of it, heights every 5 cm from the road's middle out to 9 m on his right.</summary>
        static void Curbs4Section(CityMap map, StringBuilder log, Vector3 eye, Quaternion rot)
        {
            // (the road runs at about yaw 9 deg from his eye; his line across the pan is some 20 m ahead)
            var at = new Vector2(eye.x, eye.z) + new Vector2(Mathf.Sin(9f * Mathf.Deg2Rad), Mathf.Cos(9f * Mathf.Deg2Rad)) * 20f;
            CityMap.Edge be = null; float bd = float.MaxValue, bs = 0f;
            foreach (var e in map.edges)
            {
                if (e.a == e.b || e.length < 30f || e.link) continue;
                CityElevation.ProjectOn(e, at, out float sp);
                sp = Mathf.Clamp(sp, Mathf.Min(8f, e.length * 0.5f), Mathf.Max(e.length - 8f, e.length * 0.5f));
                float d = Vector2.Distance(e.PointAt(sp), at);
                if (d < bd) { bd = d; be = e; bs = sp; }
            }
            if (be == null) return;
            Physics.SyncTransforms();
            // four stations 12 m apart from the one nearest his line (the same in every run: the map's own)
            for (int st = 0; st < 4; st++)
            {
                float ss = Mathf.Clamp(bs + st * 12f, 4f, be.length - 4f);
                var t2 = be.TangentAt(ss).normalized;
                var right = new Vector2(t2.y, -t2.x);
                var camRight = rot * Vector3.right;
                if (Vector2.Dot(right, new Vector2(camRight.x, camRight.z)) < 0f) right = -right;
                var p = be.PointAt(ss);
                float y0 = be.YAt(ss);
                var sb = new StringBuilder();
                float road = float.NaN;
                for (float d = 0f; d <= 9.001f; d += 0.05f)
                {
                    var q = p + right * d;
                    float top = float.NaN;
                    foreach (var h in Physics.RaycastAll(new Vector3(q.x, y0 + 3f, q.y), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.collider.attachedRigidbody != null) continue;
                        if (float.IsNaN(top) || h.point.y > top) top = h.point.y;
                    }
                    if (float.IsNaN(road) && !float.IsNaN(top)) road = top;
                    sb.Append(float.IsNaN(top) ? "x " : ((top - road) * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " ");
                }
                log.Append($"SECTION e{be.index} '{be.name}' cls {be.cls} s {ss:0} (class {CityMeshes.CurbClassOf(map, be)}, strip {CityMeshes.StripOf(map, be):0.00}): collider heights in cm over the road's middle, every 5 cm out to 9 m on his right:\n{sb}\n");
            }
        }
    }
}
