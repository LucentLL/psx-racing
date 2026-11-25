using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// c7roads (2026-11-25): the owner's five University City loop spots (venue CltC7), as the
    /// synced code draws them.
    ///   -executeMethod PSXRacing.EditorTools.CityC7Probe.Run   (no -nographics)
    ///   PSX_C7_POSES=file  rows "id x z heading [views]" (tab separated; heading in degrees, 0 north, 90 east;
    ///                      views comma separated from chase_night, chase_noon, plan, bumper_noon, cockpit_noon;
    ///                      default chase_night,chase_noon,plan); rows starting "inv" in views also take the
    ///                      inventory of every drawn triangle within PSX_C7_R metres (default 40)
    ///   PSX_C7_TAG=name    appended to every file
    ///   PSX_C7_PLAN=half   the plan view's half height in metres (default 40)
    /// Output (sandbox root): Screenshots/c7/&lt;id&gt;_&lt;view&gt;_&lt;tag&gt;.png, c7_probe_&lt;tag&gt;.txt (edges and nodes near each
    /// spot), c7_inv_&lt;tag&gt;.tsv (one row a piece: the spot, the mesh slot, the emitter (method, file:line and its
    /// two callers), the triangles, the piece's box, its rise over the road, its vertical face area, drawn or not).
    /// The inventory hooks CityMeshes.ProvHook while the tiles round the spot are built live (cache off).
    /// </summary>
    public static class CityC7Probe
    {
        static void Dress()
        {
            var sd = Object.FindAnyObjectByType<SeasonDress>();
            if (sd == null) return;
            var reg = typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            reg?.Invoke(sd, null);
            sd.Apply(Seasons.CurrentDress);
        }

        // ---- the inventory recorder ---------------------------------------------------------------------
        struct Rec { public string slot, who; public Vector3 a, b, c; }
        static readonly List<Rec> recs = new List<Rec>();
        static readonly Dictionary<string, string> whoCache = new Dictionary<string, string>();
        static Vector2[] spotsXZ = new Vector2[0];
        static float spotR = 40f;

        static string Frame(System.Diagnostics.StackFrame f)
        {
            var m = f.GetMethod();
            string n = m == null ? "?" : m.Name;
            string t = m?.DeclaringType?.Name ?? "";
            int lt = n.IndexOf('<'), gt = n.IndexOf('>');
            if (lt >= 0 && gt > lt) n = n.Substring(lt + 1, gt - lt - 1);
            else if (t.StartsWith("<")) { int g2 = t.IndexOf('>'); if (g2 > 1) n = t.Substring(1, g2 - 1); }
            string file = f.GetFileName();
            file = file == null ? "?" : Path.GetFileName(file);
            return n + " (" + file + ":" + f.GetFileLineNumber() + ")";
        }

        static void Hook(object bucket, List<int> tl, int t0)
        {
            var v = CityMeshes.ProvVerts(bucket);
            if (v == null || tl == null) return;
            var o = CityMeshes.ProvTileOrigin;
            bool near = false;
            for (int i = t0; i < tl.Count && !near; i++)
            {
                int vi = tl[i]; if (vi < 0 || vi >= v.Count) continue;
                var p = v[vi] + o;
                foreach (var s in spotsXZ) if ((new Vector2(p.x, p.z) - s).sqrMagnitude < (spotR + 5f) * (spotR + 5f)) { near = true; break; }
            }
            if (!near) return;
            var st = new System.Diagnostics.StackTrace(1, true);
            var sb = new StringBuilder();
            int got = 0;
            for (int i = 0; i < st.FrameCount && got < 3; i++)
            {
                var f = st.GetFrame(i);
                var m = f.GetMethod();
                string tn = m?.DeclaringType?.Name ?? "";
                if (tn == "Bucket" || m?.Name == "EarcutInto" || m?.Name == "StripTris") continue;
                if (got > 0) sb.Append(" < ");
                sb.Append(Frame(f));
                got++;
            }
            string who = sb.ToString();
            string slot = CityMeshes.ProvBucketName(bucket);
            int sl;
            if (int.TryParse(slot, out sl)) slot = SlotName(sl);
            for (int i = t0; i + 2 < tl.Count; i += 3)
            {
                int ia = tl[i], ib = tl[i + 1], ic = tl[i + 2];
                if (ia >= v.Count || ib >= v.Count || ic >= v.Count) continue;
                recs.Add(new Rec { slot = slot, who = who, a = v[ia] + o, b = v[ib] + o, c = v[ic] + o });
            }
        }

        static string SlotName(int s)
        {
            int rf = (int)CityMeshes.Slot.RoadFirst, sw = (int)CityMeshes.Slot.Sidewalk, rx = (int)CityMeshes.Slot.RoadXFirst;
            int sc = CityMeshes.SurfaceCount;
            if (s >= rf && s < sw) { int k = s - rf; return "Road p" + (k / sc) + " surf" + (k % sc); }
            if (s >= rx) { int k = s - rx; return "RoadX p" + (RoadProfiles.BaseCount + k / sc) + " surf" + (k % sc); }
            return ((CityMeshes.Slot)s).ToString();
        }

        static long Q(float f) => (long)Mathf.Round(f * 50f);   // 2 cm
        static string TriKey(Vector3 a, Vector3 b, Vector3 c)
        {
            var k = new[] { (Q(a.x), Q(a.y), Q(a.z)), (Q(b.x), Q(b.y), Q(b.z)), (Q(c.x), Q(c.y), Q(c.z)) };
            System.Array.Sort(k);
            return k[0] + "|" + k[1] + "|" + k[2];
        }

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            float planHalf = 40f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_C7_PLAN") ?? "40", NumberStyles.Float, inv, out planHalf);
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_C7_R") ?? "40", NumberStyles.Float, inv, out spotR);
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder($"C7 PROBE {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            var tsv = new StringBuilder("spot\tslot\temitter\ttris\tdrawn\tcx\tcz\tsizeX\tsizeZ\tyMinRel\tyMaxRel\tvertArea\tupArea\tdownArea\toverPave\triseOverPave\n");
            var rtsv = new StringBuilder("spot\tpath\tmesh\tsubmesh\tmaterial\ttrisNear\tbounds\n");
            int code = 0;
            try
            {
                string file = System.Environment.GetEnvironmentVariable("PSX_C7_POSES");
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new System.Exception("PSX_C7_POSES names no file");
                var rows = File.ReadAllLines(file).Where(r => r.Length > 0 && !r.StartsWith("#")).Select(r => r.Split('\t')).Where(c => c.Length >= 4).ToList();
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                var path = Object.FindAnyObjectByType<TrackPath>();
                log.Append($"tile cache {(CityTileCache.Enabled ? "ON" : "off")}\n");
                foreach (var c in rows)
                {
                    string id = c[0];
                    float x = float.Parse(c[1], inv), z = float.Parse(c[2], inv), hdg = float.Parse(c[3], inv);
                    string[] views = c.Length > 4 && c[4].Trim().Length > 0 ? c[4].Trim().Split(',') : new[] { "chase_night", "chase_noon", "plan" };
                    var p2 = new Vector2(x, z);
                    float y = 0f;
                    if (map.NearestRoadPoint(p2, 40f, false, out int ei0, out float s0, out float d0)) y = map.edges[ei0].YAt(s0);
                    var fwd = new Vector3(Mathf.Sin(hdg * Mathf.Deg2Rad), 0f, Mathf.Cos(hdg * Mathf.Deg2Rad));
                    var rot = Quaternion.LookRotation(fwd, Vector3.up);
                    var pos = new Vector3(x, y + 0.35f, z);
                    log.Append(string.Format(inv, "== {0} car ({1:0.0},{2:0.00},{3:0.0}) heading {4:0.0}; nearest edge e{5} '{6}' s {7:0.0} off {8:0.0}\n",
                        id, x, y, z, hdg, ei0, ei0 >= 0 ? map.edges[ei0].name : "", s0, d0));
                    // the edges and nodes near the spot
                    for (int e = 0; e < map.edges.Length; e++)
                    {
                        var E = map.edges[e];
                        float best = 1e9f, bs = 0f;
                        for (int k = 0; k + 1 < E.pts.Length; k++)
                        {
                            var a = E.pts[k]; var b = E.pts[k + 1]; var ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                            float dd = (a + ab * t - p2).magnitude;
                            if (dd < best) { best = dd; bs = E.s[k] + t * ab.magnitude; }
                        }
                        if (best > 70f) continue;
                        log.Append(string.Format(inv, "  e{0} '{1}' n{2}->n{3} len {4:0.0} d {5:0.0} at s {6:0.0}: cls {7} link {8} oneway {9} lanes {10} turnLane {11} width {12:0.00} prof {13} shl {14:0.0} shr {15:0.0} kmh {16} way {17} lset {18} NF {19} NB {20} ctr {21} flags {22} sub {23} turnOnly 0x{24:X} lm {25:0.00}/{26:0.00}/{27:0.00} pts {28}\n",
                            e, E.name, E.a, E.b, E.length, best, bs, E.cls, E.link, E.oneway, E.lanes, E.turnLane, E.width, E.profile, E.shl, E.shr, E.speedKmh, E.wayId,
                            E.hasLset, E.lsNF, E.lsNB, E.lsCentre, E.lsFlags, E.lsSub, E.lsTurnOnly, E.lmOff, E.lmPlus, E.lmMinus,
                            string.Join(" ", E.pts.Select(q => q.x.ToString("0.0", inv) + "," + q.y.ToString("0.0", inv)))));
                    }
                    for (int n = 0; n < map.nodes.Length; n++)
                    {
                        float dn = (map.nodes[n] - p2).magnitude;
                        if (dn > 70f) continue;
                        log.Append(string.Format(inv, "  n{0} ({1:0.0},{2:0.0}) d {3:0.0} edges {4}\n", n, map.nodes[n].x, map.nodes[n].y, dn, string.Join(",", map.nodeEdges[n])));
                    }

                    bool doInv = views.Contains("inv");
                    if (doInv) { recs.Clear(); spotsXZ = new[] { p2 }; CityMeshes.ProvHook = Hook; }
                    RaceHandoff.CalendarDay = ColourSpots.DayIn(Season.Winter);
                    RaceHandoff.WeatherOverride = (int)Weather.Clear;
                    Dress();
                    if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                    world.DropAll();
                    world.EnsureRing(pos, 1);
                    world.EnsureRing(pos + fwd * 60f, 1);
                    CityMeshes.ProvHook = null;
                    Dress();
                    if (doInv) Inventory(id, p2, map, tsv, rtsv, log);

                    foreach (var view in views)
                    {
                        if (view == "inv") continue;
                        bool night = view.EndsWith("_night");
                        int hour = night ? TimeOfDay.Night : TimeOfDay.Noon;
                        TimeOfDay.Apply(hour, sun);
                        if (globals != null) globals.Apply();
                        var hd = TimeOfDay.At(hour);
                        NightGlow.PreviewAll(hd.lightsOn);
                        bool lit = hd.lightsOn;
                        foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(lit, false);
                        PSXTexDecode.StampAll();
                        float nearWas = cam.nearClipPlane;
                        cam.ResetProjectionMatrix();
                        cam.orthographic = false;
                        Vector3 eye; Quaternion look; float shift = 0f;
                        if (view == "plan")
                        {
                            eye = new Vector3(x, y + 45f, z);
                            look = Quaternion.LookRotation(Vector3.down, fwd); eye.y = y + 45f;
                            cam.orthographic = true;
                            cam.orthographicSize = planHalf;
                            cam.nearClipPlane = 39f;   // nothing more than 6 m over the car (canopies, decks, wires): the ground plan
                        }
                        else
                        {
                            var v = view.StartsWith("bumper") ? ChaseCamera.View.Bumper : view.StartsWith("cockpit") ? ChaseCamera.View.Cockpit : ChaseCamera.View.Chase;
                            ChaseCamera.SteadyPose(v, 16f / 9f, 0f, ChaseCamera.DefaultSpeedFullMps, player.transform,
                                                   ChaseCamera.FrameOf(player), default, out eye, out look, out float fov, out shift);
                            cam.fieldOfView = fov;
                        }
                        cam.transform.SetPositionAndRotation(eye, look);
                        if (globals != null && view != "plan")
                        {
                            globals.adapt = ExposureAdapt.SteadyFor(eye, player.transform, sun != null ? -sun.transform.forward : Vector3.up,
                                                                    globals.night, path, def, globals.dayFill);
                            globals.Apply();
                        }
                        CarLights.PushGlobals();
                        StreetLights.Push(eye, look * Vector3.forward);
                        // the plan: no sun shadows (a pole's or a tree card's shadow from straight above reads as a black slab)
                        var shadowsWas = sun != null ? sun.shadows : LightShadows.None;
                        if (view == "plan" && sun != null) sun.shadows = LightShadows.None;
                        SunShadows.RenderFor(cam);
                        cam.aspect = 16f / 9f;
                        if (Mathf.Abs(shift) > 1e-5f) cam.projectionMatrix = ChaseCamera.ShiftedProjection(cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane, shift);
                        PSXScreenshotTool.ShotAs(cam, $"c7/{id}_{view}_{tag}", eye, look);
                        cam.ResetProjectionMatrix(); cam.ResetAspect(); cam.orthographic = false;
                        if (sun != null) sun.shadows = shadowsWas;
                        cam.nearClipPlane = nearWas;
                    }
                }
                log.Append("C7 PROBE OK\n");
            }
            catch (System.Exception ex) { log.Append("C7 PROBE FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshes.ProvHook = null;
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(root, $"c7_probe_{tag}.txt"), log.ToString());
                File.WriteAllText(Path.Combine(root, $"c7_inv_{tag}.tsv"), tsv.ToString());
                File.WriteAllText(Path.Combine(root, $"c7_rend_{tag}.tsv"), rtsv.ToString());
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The recorded triangles near the spot, as pieces: grouped by slot and emitter, then by
        /// shared vertices; each with its box, its rise over the nearest road surface and its face areas;
        /// drawn = found in a live tile mesh.</summary>
        static void Inventory(string id, Vector2 p2, CityMap map, StringBuilder tsv, StringBuilder rtsv, StringBuilder log)
        {
            var inv = CultureInfo.InvariantCulture;
            // the drawn triangles: every mesh renderer whose bounds come within the radius
            var drawn = new HashSet<string>();
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                var bnd = mr.bounds;
                var cxz = new Vector2(Mathf.Clamp(p2.x, bnd.min.x, bnd.max.x), Mathf.Clamp(p2.y, bnd.min.z, bnd.max.z));
                if ((cxz - p2).magnitude > spotR) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var m = mf.sharedMesh; var tr = mf.transform;
                var vs = m.vertices;
                var wv = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) wv[i] = tr.TransformPoint(vs[i]);
                var mats = mr.sharedMaterials;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    var t = m.GetTriangles(sm);
                    int nearN = 0;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = wv[t[i]], b = wv[t[i + 1]], c = wv[t[i + 2]];
                        var cc = (a + b + c) / 3f;
                        if ((new Vector2(cc.x, cc.z) - p2).magnitude > spotR) continue;
                        nearN++;
                        drawn.Add(TriKey(a, b, c));
                    }
                    if (nearN == 0) continue;
                    string pth = mr.name; var pp = mr.transform.parent; int dep = 0;
                    while (pp != null && dep < 3) { pth = pp.name + "/" + pth; pp = pp.parent; dep++; }
                    rtsv.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\n", id, pth, m.name, sm, sm < mats.Length && mats[sm] != null ? mats[sm].name : "-", nearN, bnd.ToString()));
                }
            }
            // the drawn pavement under the spot (recorded road triangles, 0.25 m cells, lowest)
            var pave = new Dictionary<long, float>();
            foreach (var r in recs)
            {
                if (!r.slot.StartsWith("Road")) continue;
                float minx = Mathf.Min(r.a.x, Mathf.Min(r.b.x, r.c.x)), maxx = Mathf.Max(r.a.x, Mathf.Max(r.b.x, r.c.x));
                float minz = Mathf.Min(r.a.z, Mathf.Min(r.b.z, r.c.z)), maxz = Mathf.Max(r.a.z, Mathf.Max(r.b.z, r.c.z));
                float den = (r.b.z - r.c.z) * (r.a.x - r.c.x) + (r.c.x - r.b.x) * (r.a.z - r.c.z);
                if (Mathf.Abs(den) < 1e-9f) continue;
                for (int j = Mathf.FloorToInt(minz * 4f); j <= Mathf.FloorToInt(maxz * 4f); j++)
                    for (int i = Mathf.FloorToInt(minx * 4f); i <= Mathf.FloorToInt(maxx * 4f); i++)
                    {
                        float x = (i + 0.5f) / 4f, z = (j + 0.5f) / 4f;
                        float l1 = ((r.b.z - r.c.z) * (x - r.c.x) + (r.c.x - r.b.x) * (z - r.c.z)) / den;
                        float l2 = ((r.c.z - r.a.z) * (x - r.c.x) + (r.a.x - r.c.x) * (z - r.c.z)) / den;
                        float l3 = 1f - l1 - l2;
                        if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                        float y = l1 * r.a.y + l2 * r.b.y + l3 * r.c.y;
                        long key = ((long)i << 32) ^ (uint)j;
                        if (!pave.TryGetValue(key, out float py) || y < py) pave[key] = y;
                    }
            }
            bool PaveY(Vector3 p, out float py) => pave.TryGetValue(((long)Mathf.FloorToInt(p.x * 4f) << 32) ^ (uint)Mathf.FloorToInt(p.z * 4f), out py);
            // group the recorded triangles: slot + emitter
            var groups = new Dictionary<string, List<Rec>>();
            foreach (var r in recs)
            {
                var cc = (r.a + r.b + r.c) / 3f;
                if ((new Vector2(cc.x, cc.z) - p2).magnitude > spotR) continue;
                string k = r.slot + "\t" + r.who;
                if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<Rec>();
                l.Add(r);
            }
            int pieces = 0;
            foreach (var kv in groups.OrderBy(g => g.Key))
            {
                var l = kv.Value;
                // pieces: union-find over shared (2 cm) vertices
                var parent = new int[l.Count];
                for (int i = 0; i < l.Count; i++) parent[i] = i;
                int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
                var owner = new Dictionary<(long, long, long), int>();
                for (int i = 0; i < l.Count; i++)
                    foreach (var p in new[] { l[i].a, l[i].b, l[i].c })
                    {
                        var key = (Q(p.x), Q(p.y), Q(p.z));
                        if (owner.TryGetValue(key, out int j)) { int ri = Find(i), rj = Find(j); if (ri != rj) parent[ri] = rj; }
                        else owner[key] = i;
                    }
                var byRoot = new Dictionary<int, List<int>>();
                for (int i = 0; i < l.Count; i++) { int r = Find(i); if (!byRoot.TryGetValue(r, out var bl)) byRoot[r] = bl = new List<int>(); bl.Add(i); }
                foreach (var piece in byRoot.Values.OrderByDescending(q => q.Count))
                {
                    Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
                    float vert = 0f, up = 0f, down = 0f; int dn = 0;
                    int overN = 0, vN = 0; float rise = -99f;
                    foreach (int i in piece)
                    {
                        var r = l[i];
                        mn = Vector3.Min(mn, Vector3.Min(r.a, Vector3.Min(r.b, r.c)));
                        mx = Vector3.Max(mx, Vector3.Max(r.a, Vector3.Max(r.b, r.c)));
                        var cr = Vector3.Cross(r.b - r.a, r.c - r.a);
                        float area = cr.magnitude * 0.5f;
                        if (area < 1e-8f) continue;
                        float ny = cr.y / (2f * area);
                        if (Mathf.Abs(ny) < 0.5f) vert += area; else if (ny > 0f) up += area; else down += area;   // the stored order's cross is the drawn face's normal
                        if (drawn.Contains(TriKey(r.a, r.b, r.c))) dn++;
                        if (!r.slot.StartsWith("Road"))
                            foreach (var p in new[] { r.a, r.b, r.c })
                            {
                                vN++;
                                if (PaveY(p, out float py)) { overN++; rise = Mathf.Max(rise, p.y - py); }
                            }
                    }
                    var ctr = (mn + mx) * 0.5f;
                    float ry = 0f;
                    if (map.NearestRoadPoint(new Vector2(ctr.x, ctr.z), 30f, false, out int ei, out float s, out float d)) ry = map.edges[ei].YAt(s);
                    tsv.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4:0.00}\t{5:0.0}\t{6:0.0}\t{7:0.0}\t{8:0.0}\t{9:0.000}\t{10:0.000}\t{11:0.00}\t{12:0.0}\t{13:0.0}\t{14:0.00}\t{15:0.000}\n",
                        id, kv.Key.Split('\t')[0], kv.Key.Split('\t')[1], piece.Count, piece.Count > 0 ? dn / (float)piece.Count : 0f, ctr.x, ctr.z, mx.x - mn.x, mx.z - mn.z,
                        mn.y - ry, mx.y - ry, vert, up, down, vN > 0 ? overN / (float)vN : 0f, overN > 0 ? rise : 0f));
                    pieces++;
                }
            }
            log.Append($"  islands so far: {CityMeshes.IslandCount} ({CityMeshes.IslandAreaM2:0} m2), tips paved {CityMeshes.IslandTipCells} cells, overlays out {CityMeshes.IslandOverlayTris}, inside out {CityMeshes.IslandInsideTris}, poke verts {CityMeshes.IslandPokeVerts}, skipped border {CityMeshes.IslandSkippedBorder} big {CityMeshes.IslandSkippedBig}\n");
            log.Append($"  inventory: {recs.Count} recorded triangles, {groups.Count} slot+emitter groups, {pieces} pieces, {drawn.Count} drawn triangles in the radius\n");
        }
    }
}
