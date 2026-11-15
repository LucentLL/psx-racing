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
    /// THE LOT ENTRANCE PICTURES (lot entrances, 2026-11-15; second pass: a
    /// TRUE before / after, one process each). The poses are fixed street
    /// places, so the same poses can be shot by a process started with
    /// PSX_CITY_LOTENT=0 (before) and one without (after).
    /// Places: PSX_LOTSHOT_SITES, '|' separated
    ///   "label;edge;s;side"   a street place and the side its lot is on
    ///   "label;@x,z"          the entrance whose mouth is nearest (after only;
    ///                         the log's SITES line gives it as edge;s;side)
    /// Poses (PSX_LOTSHOT_POSES, default all): "owner" - in the roadway, in
    /// the far lanes, looking ahead-left at the lot as the owner's picture
    /// does; "eye" - the driver's eye in the near lane 16 m before the
    /// entrance; "square" - from the far side of the street, square on;
    /// "in" - from over the lot looking back; "plan" - straight down, 24 m
    /// across the street's line (orthographic; the log's PLAN line gives the
    /// frame for the dimensions).
    /// PSX_LOTSHOT_TAG names the pass ("before" / "after": the file suffix);
    /// without it the old in-session pair is shot (the pass hidden / shown).
    /// PSX_LOTSHOT_DIR: the folder under Screenshots (default "lots");
    /// PSX_LOTSHOT_KEEP=1: the folder's other frames are kept.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunLots
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunLots()
        {
            var I = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_TAG");
            string sub = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_DIR");
            if (string.IsNullOrEmpty(sub)) sub = "lots";
            var log = new StringBuilder("LOT ENTRANCE SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " tag " + tag + ", lot entrances " + (CityMeshes.LotEntOn ? "ON" : "OFF") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", sub);
            Directory.CreateDirectory(dir);
            if (System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_KEEP") != "1")
                foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            string posesEnv = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_POSES");
            var want = new HashSet<string>((string.IsNullOrEmpty(posesEnv) ? "owner,ownerA,ownerB,ownerC,eye,square,in,plan" : posesEnv).Split(','));
            int code = 0, shots = 0;
            try
            {
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindAnyObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                string env = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_SITES");
                if (string.IsNullOrEmpty(env)) throw new System.Exception("PSX_LOTSHOT_SITES not set");
                int n = 0;
                var flick = new StringBuilder(); var sites = new StringBuilder();
                {
                    // (finding a place by what stands there) the food props and the gabled footprints within r of a point
                    string fn = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_FOODNEAR");
                    if (!string.IsNullOrEmpty(fn))
                    {
                        var c = fn.Split(',');
                        var at = new Vector2(float.Parse(c[0], I), float.Parse(c[1], I)); float rr = float.Parse(c[2], I);
                        foreach (var kv in CityBuildings.Precompute(map))
                            foreach (var b in kv.Value)
                                if (b.kind != 0 && (b.pos - at).magnitude < rr)
                                    log.Append(string.Format(I, "PROP kind {0}{1} at ({2:0.0}, {3:0.0}) yaw {4:0} deg, {5:0.0} x {6:0.0} m\n", b.kind, CityProps.IsFood(b.kind) ? " FOOD" : "", b.pos.x, b.pos.y, b.yaw * Mathf.Rad2Deg, b.w, b.d));
                        foreach (var fp in map.footprints)
                            if (fp.gable && !fp.replaced && (fp.centre - at).magnitude < rr && fp.hu * fp.hv * 4f > 150f)
                                log.Append(string.Format(I, "GABLE footprint at ({0:0.0}, {1:0.0}) {2:0.0} x {3:0.0} m\n", fp.centre.x, fp.centre.y, fp.hu * 2f, fp.hv * 2f));
                        for (int li = 0; li < map.lots.Length; li++)
                        {
                            var L = map.lots[li];
                            if (L.ring == null || L.ring.Length < 3 || L.aisleOnly) continue;
                            var lc = new Vector2(0.5f * (L.box.x + L.box.z), 0.5f * (L.box.y + L.box.w));
                            if ((lc - at).magnitude < rr) log.Append(string.Format(I, "LOT {0} at ({1:0.0}, {2:0.0}) box {3:0} x {4:0} m\n", li, lc.x, lc.y, L.box.z - L.box.x, L.box.w - L.box.y));
                        }
                    }
                }
                foreach (var spec in env.Split('|'))
                {
                    var f = spec.Split(';');
                    if (f.Length >= 2 && f[1].StartsWith("@"))
                    {
                        var c = f[1].Substring(1).Split(',');
                        int en = CityMeshes.LotEntranceNear(map, new Vector2(float.Parse(c[0], I), float.Parse(c[1], I)), out _);
                        if (en < 0) { log.Append($"{n}\t{f[0]}\tno entrance near\n"); n++; continue; }
                        var E = map.lotEntrances[en];
                        f = new[] { f[0], E.edge.ToString(I), E.s.ToString("0.0", I), ((int)E.side).ToString(I) };
                    }
                    if (f.Length < 4) continue;
                    var e = map.edges[int.Parse(f[1], I)];
                    float s = Mathf.Clamp(float.Parse(f[2], I), 0f, e.length);
                    int side = int.Parse(f[3], I);
                    sites.Append(sites.Length > 0 ? "|" : "").Append(f[0]).Append(';').Append(e.index).Append(';').Append(s.ToString("0.0", I)).Append(';').Append(side);
                    var p = e.PointAt(s); var t = e.TangentAt(s);
                    var left = LineModel.SectionLeft(e, s);
                    LineModel.Extents(e, s, out float eMinus, out float ePlus);
                    var nrm = left * side;
                    var mouth = p + nrm * (side > 0 ? ePlus : eMinus);
                    float y = e.YAt(s);
                    // the lot's asphalt along the line (any lot's own: not a corridor's)
                    float gap = 3f;
                    for (float d = 0f; d <= 62f; d += 0.25f)
                    {
                        int li = CityMeshes.LotPavedForTools(map, mouth + nrm * d);
                        if (li >= 0 && !map.lots[li].aisleOnly) { gap = d; break; }
                    }
                    // right-hand traffic: the lane beside this edge runs with the lot on its right
                    var fwd2 = t * -side;
                    Vector3 M = new Vector3(mouth.x, y, mouth.y), N = new Vector3(nrm.x, 0f, nrm.y), F = new Vector3(fwd2.x, 0f, fwd2.y);
                    float roadW = eMinus + ePlus;
                    var poses = new List<(string name, Vector3 eye, Quaternion r, bool ortho)>();
                    // the owner's: in the far lanes, heading the other way, looking ahead-left across the street at the lot
                    Vector3 ow = M - N * Mathf.Min(roadW - 1.6f, 6.2f) + F * 8f + Vector3.up * 1.25f;
                    poses.Add(("owner", ow, Quaternion.LookRotation(M + N * (gap * 0.4f + 5f) - F * 2f + Vector3.up * 0.4f - ow), false));
                    // (the owner's own picture: on the centre line before the lot, turned toward it - three tries)
                    for (int v = 0; v < 3; v++)
                    {
                        Vector3 oc = M - N * Mathf.Min(roadW * 0.5f + 0.5f, 5f) + F * (10f + 4f * v) + Vector3.up * 1.25f;
                        poses.Add(("owner" + (char)('A' + v), oc, Quaternion.LookRotation(M + N * 12f + F * (2f * v - 2f) + Vector3.up * 0.6f - oc), false));
                    }
                    Vector3 eye = M - N * 1.8f - F * 16f + Vector3.up * 1.15f;
                    poses.Add(("eye", eye, Quaternion.LookRotation(M + N * (gap * 0.5f + 1.5f) + Vector3.up * 0.2f - eye), false));
                    Vector3 sq = M - N * Mathf.Min(roadW - 0.8f, 9f) + Vector3.up * 1.5f - F * 2.5f;
                    poses.Add(("square", sq, Quaternion.LookRotation(M + N * (gap + 2f) - sq), false));
                    Vector3 inl = M + N * (gap + 9f) + F * 3f + Vector3.up * 2.6f;
                    poses.Add(("in", inl, Quaternion.LookRotation(M + N * (gap * 0.3f) - inl), false));
                    // the plan: straight down, the lot at the top, 24 m across the street's line
                    Vector3 pc = M + N * 4f;
                    poses.Add(("plan", pc + Vector3.up * 80f, Quaternion.LookRotation(Vector3.down, N), true));
                    float halfE = CityMeshes.LotEntThroatHalf;
                    { int en0 = CityMeshes.LotEntranceNear(map, mouth, out float dn0); if (en0 >= 0 && dn0 < 3f) halfE = map.lotEntrances[en0].half; }
                    float planHalf = 6.75f;   // metres up and down the picture (13.5 m tall, 24 m wide at 16:9)
                    log.Append(string.Format(I, "{0}\t{1}\te{2} '{3}' cls {4} s {5:0} side {6} walk {7}; mouth ({8:0.0}, {9:0.0}); the lot's asphalt {10:0.0} m from the pavement\n",
                        n, f[0], e.index, e.name, e.cls, s, side, CityMeshes.WalkSide(map, e, side), mouth.x, mouth.y, gap));
                    log.Append(string.Format(I, "PLAN\t{0}\t{1:0.000}\t{2:0.000}\t{3:0.0000}\t{4:0.0000}\t{5:0.000}\t{6:0.00}\t{7:0.00}\t{8:0.00}\t{9}\t{10:0.00}\n",
                        n, pc.x, pc.z, N.x, N.z, planHalf, gap, halfE, CityMeshes.LotEntTaperM, CityMeshes.WalkWidthForTools(map, e, side).ToString("0.00", I) + (CityMeshes.CurbCutForTools(map, e) ? "" : "r"), roadW));
                    {
                        var fa = M - N * 1.8f - F * 12f; var fd = (F * 12f + N * (1.8f + gap * 0.5f + 2f)).normalized;
                        flick.Append(flick.Length > 0 ? "|" : "").Append(string.Format(I, "lot{0};{1:0.0};{2:0.0};{3:0.000};{4:0.000}", n, fa.x, fa.z, fd.x, fd.z));
                    }
                    var pos = M - N * 1.8f - F * 60f + Vector3.up * 0.35f;
                    var rot = Quaternion.LookRotation(F, Vector3.up);
                    var modes = string.IsNullOrEmpty(tag) ? new[] { "after", "before" } : new[] { tag };
                    foreach (var mode in modes)
                    {
                        if (string.IsNullOrEmpty(tag)) CityMeshes.LotEntSet(map, mode == "after");
                        world.DropAll();
                        world.EnsureRing(M, 1);
                        if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                        RaceHandoff.CalendarDay = 0;
                        RaceHandoff.WeatherOverride = -1;
                        int hour = HourOf("noon");
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        PSXTexDecode.StampAll();
                        foreach (var ps in poses)
                        {
                            if (!want.Contains(ps.name)) continue;
                            bool wasOrtho = cam.orthographic; float wasSize = cam.orthographicSize;
                            if (ps.ortho) { cam.orthographic = true; cam.orthographicSize = planHalf; }
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"{sub}/{n}_{ps.name}_{mode}", ps.eye, ps.r);
                            if (ps.ortho) { cam.orthographic = wasOrtho; cam.orthographicSize = wasSize; }
                            shots++;
                        }
                    }
                    n++;
                }
                if (string.IsNullOrEmpty(tag)) CityMeshes.LotEntSet(map, true);
                log.Append("SITES " + sites + "\n");
                log.Append("FLICK " + flick + "\n");
                log.Append($"{shots} frames to {dir}\nLOT ENTRANCE SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("LOT ENTRANCE SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "lots_log" + (string.IsNullOrEmpty(tag) ? "" : "_" + tag) + ".txt"), log.ToString());
                Debug.Log("[LotShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
