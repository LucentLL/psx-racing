using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// APPROACH CURVES (the city-night round, 2026-11-23). The owner, after PC59:
    /// "Essentially the road is completely dark, then is illuminated as I drive
    /// closer" and "I can see their orbs of light clearly from half a mile away".
    /// Two earlier rounds passed their own meters (fast changes only; the editor's
    /// 500 m ring) and failed his eye. This is his test, in THE PLAYER with the
    /// baked 1 km tile ring: the game camera flown down a road at a fixed patch of
    /// road under a street lamp, from 1 km to 30 m, and at each distance the
    /// patch's rendered brightness (the graded frame: halation + PSX/Blit), the
    /// lamp's halo (peak, hard core and glow radii in pixels) and its cone (the
    /// air under the head against the air beside it). A light that exists at every
    /// distance draws a FLAT patch curve.
    ///
    ///   -perftour "approach=SPEC;SPEC&amp;hour=Night&amp;weather=Clear&amp;traffic=0
    ///              [&amp;apv=name=set:Class.Field:v,g:_Global:v;name2=...][&amp;adist=1000,700,...][&amp;aw=1920]"
    /// SPEC = name~lat~lon~fromLat~fromLon~RoadNameHas[~car]: the target is the field
    /// street lamp nearest the named road's point nearest (lat, lon); the camera comes
    /// to it along that road from the side of (fromLat, fromLon), on the right-hand
    /// lane, 2.2 m over the road, looking down the road. "~car": an oncoming car of
    /// the traffic kind parked at the target (headlights on, facing the camera, in the
    /// other lane), and the patch is the road 15 m in front of it.
    /// The player's own car follows the camera 12 m behind it with its lamps OFF, so
    /// the ring streams round the camera as in play and only the street's light (or
    /// the oncoming car's) is measured. Each distance is held while every variant is
    /// drawn (apv; default one variant "cur"), 24 frames apart.
    /// Files: &lt;tag&gt;_approach_&lt;name&gt;.csv, &lt;tag&gt;_ap_&lt;name&gt;_&lt;variant&gt;_&lt;dist&gt;.jpg.
    /// </summary>
    public partial class PerfTour
    {
        string approachArg;

        class ApTarget
        {
            public string name; public bool car;
            public Vector3 patch, between, head; public float headGround; public bool hasHead, hasBetween;
            public List<Vector3> path = new List<Vector3>();   // camera line, index = metres from the target (1 m steps)
            public List<Vector3> pathTan = new List<Vector3>();   // travel direction (toward the target)
            public Vector3 lane;   // the right-hand offset direction at the target
            public GameObject carGo;
        }

        static readonly float[] DefaultAp = { 1000f, 850f, 700f, 560f, 450f, 360f, 290f, 230f, 180f, 140f, 110f, 85f, 65f, 50f, 40f, 30f };

        IEnumerator Approach(CityMode mode)
        {
            var world = mode.world;
            var map = world.Map;
            car = mode.player;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            if (chase == null) chase = Object.FindAnyObjectByType<ChaseCamera>();
            string url = Request() ?? "";
            var ci = CultureInfo.InvariantCulture;
            float[] dists = DefaultAp;
            string ad = ShotLink.Query(url, "adist");
            if (!string.IsNullOrEmpty(ad))
            {
                var l = new List<float>();
                foreach (var s in ad.Split(',')) if (float.TryParse(s, NumberStyles.Float, ci, out float v)) l.Add(v);
                if (l.Count > 0) dists = l.ToArray();
            }
            int thumbW = 960;
            string aw = ShotLink.Query(url, "aw");
            if (!string.IsNullOrEmpty(aw)) int.TryParse(aw, out thumbW);
            // the variants
            var vars = new List<ProbeVariant>();
            string apv = ShotLink.Query(url, "apv") ?? "";
            foreach (var raw in apv.Split(';'))
            {
                string s = raw.Trim(); int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var pv = new ProbeVariant { name = s.Substring(0, eq) };
                foreach (var os in s.Substring(eq + 1).Split(','))
                {
                    if (os.Trim().Length == 0 || os.Trim() == "none") continue;
                    var op = MakeOp(os.Trim(), null, null);
                    if (op != null) pv.ops.Add(op);
                }
                vars.Add(pv);
            }
            if (vars.Count == 0) vars.Add(new ProbeVariant { name = "cur" });

            // the frame: the game camera's own render, halation and PSX/Blit, at the owner's size
            int W = 3832, H = 2054;
            if (!string.IsNullOrEmpty(offscreenArg))
            {
                var wh = offscreenArg.ToLowerInvariant().Split('x');
                if (wh.Length == 2) { int.TryParse(wh[0], out W); int.TryParse(wh[1], out H); }
            }
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1, name = "ApproachFull" };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "ApproachScreen" };
            rt.Create(); screen.Create();
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            float fov = cam.fieldOfView;

            foreach (var spec in approachArg.Split(';'))
            {
                var tg = MakeTarget(world, map, spec);
                if (tg == null) { Say("APPROACH " + spec + ": no target"); continue; }
                Say("APPROACH " + tg.name + " patch " + V(tg.patch) + " head " + (tg.hasHead ? V(tg.head) : "none") + " path " + tg.path.Count + " m");
                var csv = new StringBuilder();
                csv.AppendLine("name,variant,dist_m,cam_x,cam_z,patch_px_x,patch_px_y,patch_box,patch_Y,patch_occ,between_Y,between_occ,halo_peak,halo_core_px,halo_glow_px,cone_excess,frame_median_Y,field_lamps,table_lamps,ms");
                // the start: wait for the ring round the first camera place
                float startD = Mathf.Min(dists[0] + 40f, tg.path.Count - 1);
                if (chase != null) chase.enabled = false;
                Place(tg, startD, cam, fov);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    Place(tg, startD, cam, fov);
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 8f) break;
                    yield return null;
                }
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 8f) { Place(tg, startD, cam, fov); yield return null; }
                // the drive: 35 m/s between the distances (the ring streams as in play), held at each
                float d = startD;
                foreach (float want in dists)
                {
                    if (want > tg.path.Count - 2) continue;
                    while (d > want)
                    {
                        d = Mathf.Max(want, d - 35f * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
                        Place(tg, d, cam, fov);
                        yield return null;
                    }
                    foreach (var pv in vars)
                    {
                        foreach (var op in pv.ops) op.on();
                        for (int i = 0; i < 24; i++) { Place(tg, d, cam, fov); yield return null; }
                        Place(tg, d, cam, fov);
                        long c0 = PerfClock.Now();
                        Capture(cam, rt, screen, blit, tex, W, H);
                        float ms = (PerfClock.Now() - c0) / 10000f;
                        string row = Measure(tg, cam, tex, W, H, pv.name, want);
                        csv.AppendLine(row + "," + ms.ToString("0", ci));
                        bool thumb = want >= 990f || Mathf.Abs(want - 450f) < 1f || Mathf.Abs(want - 230f) < 1f || Mathf.Abs(want - 110f) < 1f || Mathf.Abs(want - 50f) < 1f || Mathf.Abs(want - 30f) < 1f;
                        if (thumb) Thumb(tex, W, H, thumbW, tag + "_ap_" + tg.name + "_" + pv.name + "_" + want.ToString("0", ci) + ".jpg");
                        for (int i = pv.ops.Count - 1; i >= 0; i--) pv.ops[i].off();
                    }
                }
                Post("approach_" + tg.name + ".csv", csv.ToString());
                if (tg.carGo != null) Destroy(tg.carGo);
                Say("APPROACH " + tg.name + " done");
            }
            if (chase != null) chase.enabled = true;
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }

        /// <summary>&amp;nshots=name~lat~lon~towardLat~towardLon~RoadNameHas~view;... : the player's car on
        /// that road heading toward (towardLat, towardLon), the game's own camera (chase / cockpit), one
        /// full-size frame per variant (apv) - the night sheet's cells. Lamps stay as in play.</summary>
        IEnumerator NightShots(CityMode mode, string arg)
        {
            var world = mode.world; var map = world.Map; car = mode.player;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            string url = Request() ?? "";
            var vars = new List<ProbeVariant>();
            foreach (var raw in (ShotLink.Query(url, "apv") ?? "").Split(';'))
            {
                string s = raw.Trim(); int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var pv = new ProbeVariant { name = s.Substring(0, eq) };
                foreach (var os in s.Substring(eq + 1).Split(',')) { var op = os.Trim().Length > 0 ? MakeOp(os.Trim(), null, null) : null; if (op != null) pv.ops.Add(op); }
                vars.Add(pv);
            }
            if (vars.Count == 0) vars.Add(new ProbeVariant { name = "cur" });
            int W = 3832, H = 2054;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 };
            rt.Create(); screen.Create();
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var ci = CultureInfo.InvariantCulture;
            foreach (var spec in arg.Split(';'))
            {
                var f = spec.Split('~');
                if (f.Length < 7) continue;
                if (!double.TryParse(f[1], NumberStyles.Float, ci, out double la) || !double.TryParse(f[2], NumberStyles.Float, ci, out double lo) ||
                    !double.TryParse(f[3], NumberStyles.Float, ci, out double tla) || !double.TryParse(f[4], NumberStyles.Float, ci, out double tlo)) continue;
                var route = FollowRoad(map, f[0], LL(la, lo), LL(tla, tlo), f[5], 300f);
                if (route == null) { Say("NSHOT " + f[0] + ": no road"); continue; }
                var e = map.edges[route.edges[0]]; bool fw = route.dirs[0] > 0;
                map.NearestRoadPoint(LL(la, lo), 300f, false, out int ei, out float s0, out _);
                if (ei != route.edges[0]) s0 = fw ? 0f : e.length;
                var q = e.PointAt(s0); var tt = e.TangentAt(s0); if (!fw) tt = -tt;
                var t3 = new Vector3(tt.x, 0f, tt.y).normalized;
                var right = Vector3.Cross(Vector3.up, t3).normalized;
                var pos = new Vector3(q.x, e.YAt(s0), q.y) + right * Mathf.Clamp(e.width * 0.25f, 1.6f, 5.5f);
                pos = Ground(pos) + Vector3.up * 0.45f;
                var rot = Quaternion.LookRotation(t3);
                if (System.Enum.TryParse(f[6], true, out ChaseCamera.View v)) ChaseCamera.PreviewView(v);
                world.EnsureRing(pos, 1);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    car.ResetTo(pos, rot, seated: true);
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 10f) break;
                    yield return null;
                }
                float sA = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - sA < 10f) { car.ResetTo(pos, rot, seated: true); yield return null; }
                foreach (var pv in vars)
                {
                    foreach (var op in pv.ops) op.on();
                    for (int i = 0; i < 30; i++) { car.ResetTo(pos, rot, seated: true); yield return null; }
                    Capture(cam, rt, screen, blit, tex, W, H);
                    var jpg = tex.EncodeToJPG(92);
                    PSXPerfTour_Post(tag + "_ns_" + f[0] + "_" + pv.name + ".jpg", jpg, jpg.Length);
                    for (int i = pv.ops.Count - 1; i >= 0; i--) pv.ops[i].off();
                }
                Say("NSHOT " + f[0] + " at " + V(pos));
            }
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }

        /// <summary>&amp;clip=SPEC (an approach SPEC)[&amp;clipsecs=30]: the camera flown down the road at the
        /// target from 1 km at a steady speed, game time stepped at 30 fps (Time.captureFramerate), every
        /// frame written (1920 wide), once per variant: the before | after clips.</summary>
        IEnumerator Clip(CityMode mode, string spec)
        {
            var world = mode.world; car = mode.player;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>() ?? Object.FindAnyObjectByType<ChaseCamera>();
            string url = Request() ?? "";
            float secs = 30f; float.TryParse(ShotLink.Query(url, "clipsecs") ?? "30", NumberStyles.Float, CultureInfo.InvariantCulture, out secs);
            var vars = new List<ProbeVariant>();
            foreach (var raw in (ShotLink.Query(url, "apv") ?? "").Split(';'))
            {
                string s = raw.Trim(); int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var pv = new ProbeVariant { name = s.Substring(0, eq) };
                foreach (var os in s.Substring(eq + 1).Split(',')) { var op = os.Trim().Length > 0 ? MakeOp(os.Trim(), null, null) : null; if (op != null) pv.ops.Add(op); }
                vars.Add(pv);
            }
            if (vars.Count == 0) vars.Add(new ProbeVariant { name = "cur" });
            int W = 1920, H = 1030;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 };
            rt.Create(); screen.Create();
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var tg = MakeTarget(world, world.Map, spec);
            if (tg == null) { Say("CLIP: no target"); yield break; }
            if (tg.carGo != null) tg.carGo.SetActive(true);
            float fov = cam.fieldOfView;
            float start = Mathf.Min(1000f, tg.path.Count - 2), speed = (start - 25f) / secs;
            if (chase != null) chase.enabled = false;
            foreach (var pv in vars)
            {
                foreach (var op in pv.ops) op.on();
                Time.captureFramerate = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    Place(tg, start, cam, fov);
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 8f) break;
                    yield return null;
                }
                for (int i = 0; i < 60; i++) { Place(tg, start, cam, fov); yield return null; }
                Time.captureFramerate = 30;
                int frames = Mathf.RoundToInt(secs * 30f);
                for (int k = 0; k < frames; k++)
                {
                    float d = start - speed * (k / 30f);
                    Place(tg, d, cam, fov);
                    yield return null;
                    Place(tg, d, cam, fov);
                    Capture(cam, rt, screen, blit, tex, W, H);
                    var jpg = tex.EncodeToJPG(85);
                    PSXPerfTour_Post(tag + "_clip_" + pv.name + "_" + k.ToString("0000") + ".jpg", jpg, jpg.Length);
                }
                Time.captureFramerate = 0;
                for (int i = pv.ops.Count - 1; i >= 0; i--) pv.ops[i].off();
                Say("CLIP " + tg.name + " " + pv.name + " " + frames + " frames");
            }
            if (chase != null) chase.enabled = true;
            if (tg.carGo != null) Destroy(tg.carGo);
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }

        static string V(Vector3 v) => v.x.ToString("0") + "/" + v.y.ToString("0.0") + "/" + v.z.ToString("0");

        /// <summary>The camera at <paramref name="d"/> metres from the target down
        /// the road, the player's car 12 m behind it (lamps off).</summary>
        void Place(ApTarget tg, float d, Camera cam, float fov)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(d), 0, tg.path.Count - 1);
            var p = tg.path[i]; var t = tg.pathTan[i];
            var look = tg.path[Mathf.Clamp(i - 40, 0, tg.path.Count - 1)];
            var eye = p + Vector3.up * 2.2f;
            var dir = look + Vector3.up * 1.2f - eye;
            if (i < 45) dir = t;
            dir.y = Mathf.Clamp(dir.y, -0.1f, 0.05f) - 0.035f * new Vector2(dir.x, dir.z).magnitude;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir.normalized, Vector3.up));
            cam.ResetProjectionMatrix();
            cam.fieldOfView = fov;
            int j = Mathf.Clamp(i + 12, 0, tg.path.Count - 1);
            if (car != null)
            {
                car.ResetTo(tg.path[j] + Vector3.up * 0.45f, Quaternion.LookRotation(new Vector3(t.x, 0f, t.z)), seated: true);
                var cl = car.GetComponentInChildren<CarLights>();
                if (cl != null && cl.enabled) cl.enabled = false;
            }
        }

        ApTarget MakeTarget(CityWorld world, CityMap map, string spec)
        {
            var f = spec.Split('~');
            if (f.Length < 6) return null;
            var ci = CultureInfo.InvariantCulture;
            if (!double.TryParse(f[1], NumberStyles.Float, ci, out double la) || !double.TryParse(f[2], NumberStyles.Float, ci, out double lo) ||
                !double.TryParse(f[3], NumberStyles.Float, ci, out double fla) || !double.TryParse(f[4], NumberStyles.Float, ci, out double flo)) return null;
            var tg = new ApTarget { name = f[0], car = f.Length > 6 && f[6] == "car" };
            Vector2 at = LL(la, lo), from = LL(fla, flo);
            // the road out from the target toward the camera's side
            var route = FollowRoad(map, tg.name, at, from, f[5], 1400f);
            if (route == null) return null;
            // sampled every metre: centreline (x, y, z) and the direction of travel AWAY from the target
            var cl = new List<Vector3>(); var tn = new List<Vector3>(); var hw = new List<float>();
            for (int k = 0; k < route.edges.Length; k++)
            {
                var e = map.edges[route.edges[k]]; bool fw = route.dirs[k] > 0;
                for (float s = 0f; s < e.length; s += 1f)
                {
                    float a = fw ? s : e.length - s;
                    var q = e.PointAt(a); var tt = e.TangentAt(a); if (!fw) tt = -tt;
                    cl.Add(new Vector3(q.x, e.YAt(a), q.y)); tn.Add(new Vector3(tt.x, 0f, tt.y).normalized); hw.Add(e.width * 0.5f);
                }
            }
            if (cl.Count < 60) return null;
            // the target lamp: the field street lamp nearest the first 60 m of road
            Vector3 c0 = new Vector3(at.x, cl[0].y, at.y);
            tg.hasHead = !tg.car && StreetLights.NearestFieldLamp(c0, 60f, out tg.head);
            int iP = 0;
            {
                float best = float.MaxValue;
                Vector3 aim = tg.hasHead ? tg.head : c0;
                for (int k = 0; k < Mathf.Min(cl.Count, 500); k++)
                {
                    float dd = new Vector2(cl[k].x - aim.x, cl[k].z - aim.z).sqrMagnitude;
                    if (dd < best) { best = dd; iP = k; }
                }
            }
            // re-based so index 0 is the patch's station, travel reversed (the camera drives TOWARD the target)
            Vector3 right0 = Vector3.Cross(Vector3.up, -tn[iP]).normalized;   // the camera's right at the target
            tg.lane = right0;
            float laneOff = Mathf.Clamp(hw[iP] * 0.5f, 1.6f, 5.5f);
            for (int k = iP; k < cl.Count; k++)
            {
                var tt = -tn[k];
                var r = Vector3.Cross(Vector3.up, tt).normalized;
                tg.path.Add(cl[k] + r * laneOff);
                tg.pathTan.Add(tt);
            }
            Vector3 cP = cl[iP];
            if (tg.car)
            {
                // an oncoming car in the far lane, facing the camera; the patch 15 m before it
                Vector3 opp = cP - right0 * laneOff;
                tg.carGo = SpawnParked(opp, tn[iP]);
                Vector3 pp = opp + tn[iP] * 15f;
                tg.patch = Ground(pp);
                tg.head = opp + Vector3.up * 0.7f + tn[iP] * 2.2f; tg.hasHead = true; tg.headGround = opp.y;
                tg.hasBetween = false;
                return tg;
            }
            // the patch: the road under the lamp, on the lamp's side, 1.5 m in from the edge
            if (tg.hasHead)
            {
                Vector3 side = new Vector3(tg.head.x - cP.x, 0f, tg.head.z - cP.z);
                float sd = Mathf.Min(side.magnitude, Mathf.Max(1f, hw[iP] - 1.5f));
                tg.patch = Ground(cP + (side.sqrMagnitude > 1e-4f ? side.normalized : right0) * sd);
                tg.headGround = Ground(new Vector3(tg.head.x, cP.y, tg.head.z)).y;
                // "~wall": the wall beside the lamp, 2.5 m up, past it away from the road
                if (f.Length > 6 && f[6] == "wall" && side.sqrMagnitude > 1e-4f)
                {
                    var o = new Vector3(tg.head.x, tg.headGround + 2.5f, tg.head.z);
                    if (Physics.Raycast(o, side.normalized, out var wh, 35f, ~(1 << 2), QueryTriggerInteraction.Ignore)) tg.patch = wh.point - side.normalized * 0.05f;
                    else Debug.Log("[PerfTour] wall target " + tg.name + ": no wall within 35 m");
                }
            }
            else tg.patch = Ground(cP);
            // the road between this lamp and the next one toward the camera (the darkest stretch)
            for (int k = iP + 6; k < Mathf.Min(cl.Count, iP + 70); k++)
                if (StreetLights.NearestFieldLamp(cl[k], 12f, out var h2) && (h2 - tg.head).sqrMagnitude > 25f)
                {
                    int mid = (iP + k) / 2;
                    Vector3 sideB = new Vector3(tg.head.x - cP.x, 0f, tg.head.z - cP.z);
                    tg.between = Ground(cl[mid] + (sideB.sqrMagnitude > 1e-4f ? sideB.normalized : right0) * Mathf.Max(1f, hw[mid] - 1.5f) * 0.6f);
                    tg.hasBetween = true;
                    break;
                }
            return tg;
        }

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return hit.point;
            return p;
        }

        static GameObject SpawnParked(Vector3 at, Vector3 fwd)
        {
            try
            {
                string key = TrafficModels.CityKey(3);
                var def = TrafficModels.Load(key);
                var go = new GameObject("ApproachOncoming_" + key);
                OnFoot.CarShell.Spawn(go.transform, def, 0, out _, solid: false);
                var box = go.AddComponent<BoxCollider>();
                box.center = def.colliderCenter; box.size = def.colliderSize;
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
                var lights = go.AddComponent<CarLights>();
                lights.box = box; lights.shellDef = def; lights.shellZ = -def.colliderCenter.z;
                var g = Ground(at);
                go.transform.SetPositionAndRotation(g + Vector3.up * 0.05f, Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z)));
                return go;
            }
            catch (System.Exception e) { Debug.Log("[PerfTour] oncoming car failed: " + e.Message); return null; }
        }

        static void Capture(Camera cam, RenderTexture rt, RenderTexture screen, Material blit, Texture2D tex, int W, int H)
        {
            var was = cam.targetTexture;
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = was; }
            RenderTexture glow = null;
            if (blit != null && Halation.Wanted(blit)) glow = Halation.BuildNow(rt);
            if (blit != null) { Halation.Apply(blit, glow); Graphics.Blit(rt, screen, blit); }
            else Graphics.Blit(rt, screen);
            var prev = RenderTexture.active;
            RenderTexture.active = screen;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            Halation.Release(ref glow);
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        static Color32[] apPix;

        string Measure(ApTarget tg, Camera cam, Texture2D tex, int W, int H, string variant, float dist)
        {
            apPix = tex.GetPixels32();
            var ci = CultureInfo.InvariantCulture;
            var eye = cam.transform.position;
            // the patch: a 3 m square on the road (its screen box, 3-80 px)
            Vector3 tt = tg.pathTan.Count > 0 ? tg.pathTan[0] : Vector3.forward;
            Vector3 rr = Vector3.Cross(Vector3.up, tt).normalized;
            float pY = BoxY(cam, tg.patch, tt, rr, W, H, out Vector2 pPx, out int box);
            bool pOcc = Occluded(eye, tg.patch);
            float bY = -1f; bool bOcc = false;
            if (tg.hasBetween) { bY = BoxY(cam, tg.between, tt, rr, W, H, out _, out _); bOcc = Occluded(eye, tg.between); }
            float peak = -1f, core = 0f, glow = 0f, cone = -999f;
            if (tg.hasHead)
            {
                var sp = cam.WorldToScreenPoint(tg.head);
                if (sp.z > 0f && sp.x >= 0 && sp.y >= 0 && sp.x < W && sp.y < H)
                {
                    int cx = (int)sp.x, cy = (int)sp.y, R = 60, nCore = 0, nGlow = 0;
                    float pk = 0f;
                    for (int y = Mathf.Max(0, cy - R); y <= Mathf.Min(H - 1, cy + R); y++)
                        for (int x = Mathf.Max(0, cx - R); x <= Mathf.Min(W - 1, cx + R); x++)
                        {
                            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > R * R) continue;
                            float l = Luma(apPix[y * W + x]);
                            if (l > pk) pk = l;
                            if (l >= 235f) nCore++;
                            if (l >= 110f) nGlow++;
                        }
                    peak = pk; core = Mathf.Sqrt(nCore / Mathf.PI); glow = Mathf.Sqrt(nGlow / Mathf.PI);
                    // the cone: the air under the head, against the air 4 m either side of it
                    if (!tg.car)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, (tg.head - eye)).normalized;
                        float hh = tg.head.y - tg.headGround;
                        for (int k = 1; k <= 3; k++)
                        {
                            Vector3 m = tg.head - Vector3.up * hh * (0.22f * k);
                            float c = PointY(cam, m, W, H), a = PointY(cam, m + side * 4f, W, H), b = PointY(cam, m - side * 4f, W, H);
                            if (c < 0f || a < 0f || b < 0f) continue;
                            cone = Mathf.Max(cone, c - 0.5f * (a + b));
                        }
                    }
                }
            }
            // the frame's median luma (a coarse grid)
            var ls = new List<float>();
            for (int y = 0; y < H; y += 16) for (int x = 0; x < W; x += 16) ls.Add(Luma(apPix[y * W + x]));
            ls.Sort();
            float med = ls.Count > 0 ? ls[ls.Count / 2] : 0f;
            return string.Join(",", new[]
            {
                tg.name, variant, dist.ToString("0", ci), eye.x.ToString("0", ci), eye.z.ToString("0", ci),
                pPx.x.ToString("0", ci), pPx.y.ToString("0", ci), box.ToString(ci), pY.ToString("0.0", ci), pOcc ? "1" : "0",
                bY.ToString("0.0", ci), bOcc ? "1" : "0", peak.ToString("0", ci), core.ToString("0.0", ci), glow.ToString("0.0", ci),
                cone.ToString("0.0", ci), med.ToString("0.0", ci), StreetLights.FieldLamps.ToString(ci), StreetLights.PushedCount.ToString(ci),
            });
        }

        static float PointY(Camera cam, Vector3 w, int W, int H)
        {
            var sp = cam.WorldToScreenPoint(w);
            if (sp.z <= 0f) return -1f;
            int x = (int)sp.x, y = (int)sp.y;
            if (x < 2 || y < 2 || x >= W - 2 || y >= H - 2) return -1f;
            float s = 0f;
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) s += Luma(apPix[(y + j) * W + x + i]);
            return s / 25f;
        }

        static float BoxY(Camera cam, Vector3 c, Vector3 t, Vector3 r, int W, int H, out Vector2 px, out int box)
        {
            px = new Vector2(-1f, -1f); box = 0;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            var sc = cam.WorldToScreenPoint(c + Vector3.up * 0.05f);
            if (sc.z <= 0f) return -1f;
            px = new Vector2(sc.x, sc.y);
            foreach (var o in new[] { t * 1.5f + r * 1.5f, t * 1.5f - r * 1.5f, -t * 1.5f + r * 1.5f, -t * 1.5f - r * 1.5f })
            {
                var s = cam.WorldToScreenPoint(c + o + Vector3.up * 0.05f);
                if (s.z <= 0f) continue;
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
            }
            int hw = Mathf.Clamp(Mathf.RoundToInt((x1 - x0) * 0.5f), 1, 40), hh = Mathf.Clamp(Mathf.RoundToInt((y1 - y0) * 0.5f), 1, 40);
            int cx = (int)sc.x, cy = (int)sc.y;
            if (cx - hw < 0 || cy - hh < 0 || cx + hw >= W || cy + hh >= H) return -1f;
            box = (2 * hw + 1) * (2 * hh + 1);
            float s2 = 0f; int n = 0;
            for (int y = cy - hh; y <= cy + hh; y++) for (int x = cx - hw; x <= cx + hw; x++) { s2 += Luma(apPix[y * W + x]); n++; }
            return s2 / Mathf.Max(1, n);
        }

        static bool Occluded(Vector3 eye, Vector3 p)
        {
            var to = p + Vector3.up * 0.25f;
            var d = to - eye; float len = d.magnitude;
            return Physics.Raycast(eye, d / len, out var hit, len - 2f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        void Thumb(Texture2D tex, int W, int H, int tw, string name)
        {
            int th = Mathf.RoundToInt(H * (tw / (float)W));
            var small = RenderTexture.GetTemporary(tw, th, 0);
            var src = new Texture2D(W, H, TextureFormat.RGB24, false);
            src.SetPixels32(apPix); src.Apply(false);
            Graphics.Blit(src, small);
            var prev = RenderTexture.active; RenderTexture.active = small;
            var t = new Texture2D(tw, th, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, tw, th), 0, 0); t.Apply(false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(small);
            var jpg = t.EncodeToJPG(88);
            Destroy(t); Destroy(src);
            PSXPerfTour_Post(name, jpg, jpg.Length);
        }
    }
}
