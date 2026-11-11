using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE COCKPIT'S DOOR MIRRORS AGAINST THE CAR'S OWN EXTERIOR MIRRORS
    /// (owner, 2026-11-11: "mirrors don't match the updated mirrors of the new
    /// car models imported").
    ///
    /// Shots(): per shell in PSX_MIRROR_SHELLS, the owner's pose from the
    /// seat (looking over to the driver's door and to the passenger's, LHD),
    /// and each mirror from OUTSIDE, the shell whole, as the chase view and the
    /// showroom see it. Written to Screenshots/MirrorMatch/&lt;PSX_MIRROR_TAG&gt;/.
    /// </summary>
    public static class DoorMirrorMatch
    {
        /// <summary>
        /// THE CHECK, to re-run after every car batch (PSX Racing/Door Mirror
        /// Match Check; batch: -executeMethod ...DoorMirrorMatch.CheckBatch,
        /// exit 1 on a failure). Every shell in Resources/CarModels measured
        /// afresh by <see cref="DoorMirrorProbe.Measure"/> - the cockpit's
        /// mirrors ARE the shell's own housings, so what can drift is the
        /// table the build reads (DoorMirrors.Table.cs): its glass and keep
        /// box against the mesh as it is now. Per mirror: the glass's centre,
        /// its width and height, its triangle count and the keep box, each
        /// within <see cref="Tol"/>; a row for a shell with no mirrors, or
        /// none for one with them, fails too. The pop-up lamps' raised body
        /// must hold the same mirrors as the body. Writes
        /// Screenshots/DoorMirrors/match.txt.
        /// </summary>
        public const float Tol = 0.01f;

        [MenuItem("PSX Racing/Door Mirror Match Check")]
        public static void CheckMenu() => Check(out _);

        public static void CheckBatch()
        {
            int fails = 0;
            try { Check(out fails); }
            catch (System.Exception e) { Debug.LogException(e); fails = -1; }
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        /// <summary>One job: the table checked under the OLD glass rule (it
        /// was written with it), then under the new one, then re-probed and
        /// written (the probe's PSX_DOORMIRROR_EMIT).</summary>
        public static void ReprobeBatch()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots", "DoorMirrors");
            System.Environment.SetEnvironmentVariable("PSX_DOORMIRROR_GLASSPART", "0");
            File.WriteAllText(Path.Combine(dir, "match_oldrule.txt"), Check(out _));
            System.Environment.SetEnvironmentVariable("PSX_DOORMIRROR_GLASSPART", "1");
            File.WriteAllText(Path.Combine(dir, "match_newrule_oldtable.txt"), Check(out _));
            System.Environment.SetEnvironmentVariable("PSX_DOORMIRROR_EMIT", "1");
            DoorMirrorProbe.Run();
            EditorApplication.Exit(0);
        }

        /// <summary>The nose-closing proof (inside off, then on) for a few
        /// shells, then the catalog check; exit 0 / 1.</summary>
        public static void ProofBatch()
        {
            int fails = -1;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_MIRROR_SHELLS", "roadster_99,flatsix_coupe_87,viper_gts,rx7_fd_93,civic_eg");
                System.Environment.SetEnvironmentVariable("PSX_COCKPIT_INSIDE", "0");
                System.Environment.SetEnvironmentVariable("PSX_MIRROR_TAG", "noinside");
                Shots();
                System.Environment.SetEnvironmentVariable("PSX_COCKPIT_INSIDE", "1");
                System.Environment.SetEnvironmentVariable("PSX_MIRROR_TAG", "inside");
                Shots();
                Check(out fails);
            }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        public static string Check(out int fails)
        {
            var sb = new StringBuilder();
            fails = 0;
            var defs = new List<CarModelDef>();
            foreach (var go in Resources.LoadAll<GameObject>("CarModels"))
            {
                var d = go != null ? go.GetComponent<CarModelDef>() : null;
                if (d != null && d.bodyMesh != null) defs.Add(d);
            }
            defs.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            int ok = 0, none = 0;
            var fitLog = new StringBuilder("\n[CockpitFit] per shell, life-size rest pose, LHD:\n");
            foreach (var d in defs)
            {
                var found = DoorMirrorProbe.Measure(d, new StringBuilder());
                bool have = DoorMirrors.TryGet(d.key, out var e);
                int rows = have ? e.glass.Length : 0;
                var why = new List<string>();
                if (rows != found.Count) why.Add($"table has {rows} mirrors, the mesh {found.Count}");
                else
                    for (int i = 0; i < rows; i++)
                    {
                        var g = e.glass[i];
                        var f = found[i];
                        string s = g.centre.x < 0f ? "L" : "R";
                        float dc = (g.centre - f.centre).magnitude;
                        float dw = Mathf.Abs(g.across.magnitude - f.across.magnitude) * 2f;
                        float dh = Mathf.Abs(g.up.magnitude - f.up.magnitude) * 2f;
                        float db = Mathf.Max((g.min - f.min).magnitude, (g.max - f.max).magnitude);
                        if (dc > Tol) why.Add($"{s} glass centre off {dc * 100f:0.0} cm");
                        if (dw > Tol || dh > Tol) why.Add($"{s} glass {g.across.magnitude * 200f:0.0}x{g.up.magnitude * 200f:0.0} cm, mesh {f.across.magnitude * 200f:0.0}x{f.up.magnitude * 200f:0.0}");
                        if (g.tris.Length / 3 != f.tris.Length / 3) why.Add($"{s} glass {g.tris.Length / 3} tris, mesh {f.tris.Length / 3}");
                        if (db > Tol) why.Add($"{s} housing box off {db * 100f:0.0} cm");
                    }
                if (d.lampsUpMesh != null)
                {
                    var up = Object.Instantiate(d);
                    up.bodyMesh = d.lampsUpMesh;
                    var fu = DoorMirrorProbe.Measure(up, new StringBuilder());
                    Object.DestroyImmediate(up.gameObject);
                    if (fu.Count != found.Count) why.Add($"lamps-up body has {fu.Count} mirrors, body {found.Count}");
                    else for (int i = 0; i < fu.Count; i++)
                            if ((fu[i].centre - found[i].centre).magnitude > Tol) why.Add("lamps-up body's mirror moves");
                }
                // THE FIT (2026-11-11): the cabin across against the car's own
                // mirrors, and the sightline over the dash against the shell
                // just ahead of the cut (positive: the line enters the hollow
                // nose under the bonnet's edge - closed by the dark inside).
                var fit = CockpitInterior.FitFor(d.colliderCenter, d.colliderSize, d);
                float sx = CockpitInterior.WidthFitFor(d, 1f, fit.scale);
                float glass0 = CockpitInterior.AuthoredGlassX * fit.scale, glass1 = glass0 * sx;
                float inner = CockpitInterior.MirrorInnerX(d, 1f);
                float slot = SlotOverCut(d, fit);
                if (inner > 0f && glass1 > inner - CockpitInterior.MirrorClear + 0.002f)
                    why.Add($"cabin glass {glass1:0.000} m outside the mirrors' inner edge {inner:0.000}");
                fitLog.Append($"{d.key,-20} scale {fit.scale:0.000} widthFit {sx:0.000} cabin glass x {glass0:0.000} -> {glass1:0.000} mirror inner x {(inner > 0f ? inner.ToString("0.000") : "-")}" +
                              $" {(inner > 0f && glass0 > inner ? "MIRROR INSIDE CABIN before" : "")}  sightline under the shell at the cut {slot * 100f:+0.0;-0.0} cm{(slot > 0f ? " (view into the nose: dark inside)" : "")}\n");
                if (found.Count == 2 && Mathf.Abs(found[0].w - found[1].w) > Tol)
                    fitLog.Append($"   note {d.key}: its two mirror glasses differ ({found[0].w * 100f:0.0} / {found[1].w * 100f:0.0} cm wide)\n");
                string line = $"{d.key,-20} mirrors {found.Count}";
                for (int i = 0; i < found.Count; i++)
                    line += $"  {(found[i].centre.x < 0f ? "L" : "R")} glass {found[i].w * 100f:0.0}x{found[i].h * 100f:0.0} cm {found[i].faceTris} tris";
                if (why.Count > 0) { fails++; sb.Append("FAIL ").Append(line).Append("  <- ").Append(string.Join("; ", why)).Append('\n'); }
                else { if (found.Count == 0) none++; else ok++; sb.Append("ok   ").Append(line).Append('\n'); }
            }
            sb.Insert(0, $"[MirrorMatch] {defs.Count} shells: {ok} with live mirrors match, {none} with no separable mirror, {fails} FAIL (tolerance {Tol * 100f:0} cm; glass rule {(DoorMirrorProbe.GlassPartRule ? "model's glass part" : "welded patch")})\n");
            sb.Append(fitLog);
            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots", "DoorMirrors");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "match.txt"), sb.ToString());
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        static void SaveRT(RenderTexture rt, string path)
        {
            var keep = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = keep;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>The shell's top just ahead of the cut (5 cm deep, a
        /// 30 cm band about the eye's x) less the height of the line over the
        /// dash there, metres.</summary>
        static float SlotOverCut(CarModelDef d, CockpitInterior.Fit fit)
        {
            var mesh = d.bodyMesh;
            if (mesh == null || !mesh.isReadable && !Application.isEditor) return 0f;
            var mv = mesh.vertices;
            var rot = Quaternion.Euler(0f, d.bodyYaw, 0f);
            var off = new Vector3(0f, d.bodyYOffset, d.bodyZOffset);
            float top = float.MinValue;
            foreach (var v in mv)
            {
                var p = rot * v + off;
                if (p.z < fit.cutZ || p.z > fit.cutZ + 0.12f || Mathf.Abs(p.x - fit.eye.x) > 0.25f) continue;
                top = Mathf.Max(top, p.y);
            }
            if (top == float.MinValue) return 0f;
            float slope = CockpitInterior.SightDrop / Mathf.Max(0.1f, CockpitInterior.ZRef);
            float lineY = fit.eye.y - slope * (fit.cutZ - fit.eye.z);
            return top - lineY;
        }

        [MenuItem("PSX Racing/Door Mirror Match Shots")]
        public static void Shots()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_MIRROR_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "now";
            var def = TrackCatalog.At(TrackCatalog.CheckVenue);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots", "MirrorMatch", tag);
            Directory.CreateDirectory(dir);
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 720;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }

            var t = player.transform;
            var car = player.GetComponent<CarController>();
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            float keepNear = cam.nearClipPlane, keepFov = cam.fieldOfView;
            var keepView = ChaseCamera.Current;
            PSXScreenshotTool.AspectOverride = 16f / 9f;
            DriveSide.Override = false;
            if (car != null) { car.currentRPM = 3000f; car.speedKmh = 70f; }

            string shells = System.Environment.GetEnvironmentVariable("PSX_MIRROR_SHELLS");
            if (string.IsNullOrEmpty(shells)) shells = "rx7_fd_93,viper_gts,flatsix_coupe,flatsix_coupe_87,rally_sedan_99,civic_eg,roadster_99,charger_70,supra_a80,bmw_e30,skyline_r32";
            var log = new StringBuilder();
            CockpitInterior ci = null;
            foreach (var key0 in shells.Split(','))
            {
                string key = key0.Trim();
                var shell = CarModelLibrary.Load(key);
                if (shell == null || body == null || shell.key != key) { Debug.LogError("[MirrorMatch] no shell " + key); continue; }
                body.Apply(shell, 0);
                Physics.SyncTransforms();
                Vector3 c = box.center, s = box.size;

                // ---- outside: the shell whole, no cabin, no cut ----
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                if (cv != null) cv.PreviewShow(false);
                if (ci != null) ci.Show(false, false);
                CabinClip.Close();
                var bodyT = body.bodyFilter != null ? body.bodyFilter.transform : t;
                var mirrors = DoorMirrorProbe.Measure(shell, new StringBuilder());
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                foreach (var f in mirrors)
                {
                    Vector3 mc = bodyT.TransformPoint((f.min + f.max) * 0.5f);
                    Vector3 local = t.InverseTransformPoint(mc);
                    float sg = Mathf.Sign(local.x);
                    Vector3 eyeL = local + new Vector3(sg * 0.55f, 0.18f, -0.85f);
                    Vector3 pos = t.TransformPoint(eyeL);
                    var rot = Quaternion.LookRotation(mc - pos, t.up);
                    PSXScreenshotTool.ShotAs(cam, $"MirrorMatch/{tag}/{key}_ext_{(sg < 0 ? "L" : "R")}", pos, rot);
                    // from ahead-outside: the housing's front and its mount
                    Vector3 eyeF = local + new Vector3(sg * 0.6f, 0.25f, 0.85f);
                    pos = t.TransformPoint(eyeF);
                    rot = Quaternion.LookRotation(mc - pos, t.up);
                    PSXScreenshotTool.ShotAs(cam, $"MirrorMatch/{tag}/{key}_extF_{(sg < 0 ? "L" : "R")}", pos, rot);
                }

                // ---- inside: the owner's pose ----
                ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[MirrorMatch] no CockpitInterior on the player"); break; }
                ci.Refit();
                ci.Show(true, true);
                ci.SetSteer(0f, 34f);
                ci.Tick(0f, null);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();
                var doors = ci.Doors;
                ci.PreviewDoorMirrors(cam);
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell, body.LengthScale, body.BodyLift, false);
                log.Append($"{key}: rig mirrors {(doors != null ? doors.Count : 0)} probe {mirrors.Count} cabinHalfW {ci.Current.cabinHalfW:0.000} scale {ci.Current.scale:0.000}\n");
                log.Append($"   widthFit {ci.Current.widthFit:0.000} eye shift {ci.EyeShiftX * 100f:0.0} cm\n");
                eye.x += ci.EyeShiftX;
                ci.PreviewMirror(cam);
                var rv = ci.MirrorTexture;
                var looks = new (string, float)[] { ("in_F", 0f), ("in_L", -42f), ("in_R", 52f) };
                foreach (var (look, yaw0) in looks)
                {
                    float yaw = yaw0 * CockpitLook.PassengerSign;
                    var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                    cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
                    ci.HideShell(true);
                    if (doors != null) doors.Pose();
                    PSXScreenshotTool.ShotAs(cam, $"MirrorMatch/{tag}/{key}_{look}", t.TransformPoint(eye), rot);
                    ci.HideShell(false);
                }
                // and down into the gap between dash and bonnet (the owner's 11-11 look)
                {
                    var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + 9f, 0f, 0f);
                    cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, 0f);
                    ci.HideShell(true);
                    PSXScreenshotTool.ShotAs(cam, $"MirrorMatch/{tag}/{key}_in_D", t.TransformPoint(eye), rot);
                    ci.HideShell(false);
                    // the eye 6 cm high against the cabin (a dive, a bump, a
                    // nose-up stance): the line over the dash passes under the
                    // bonnet's back edge into the nose
                    ci.HideShell(true);
                    PSXScreenshotTool.ShotAs(cam, $"MirrorMatch/{tag}/{key}_in_D6", t.TransformPoint(eye + new Vector3(0f, 0.06f, 0f)), rot);
                    ci.HideShell(false);
                }
                if (rv != null) SaveRT(rv, Path.Combine(dir, $"{key}_rearview.png"));
                ci.Show(false, false);
                if (cv != null) cv.PreviewShow(false);
            }
            DriveSide.Override = null;
            PSXScreenshotTool.AspectOverride = 0f;
            ChaseCamera.PreviewView(keepView);
            cam.nearClipPlane = keepNear;
            cam.fieldOfView = keepFov;
            File.WriteAllText(Path.Combine(dir, "log.txt"), log.ToString());
            Debug.Log("[MirrorMatch] " + log);
        }
    }
}
