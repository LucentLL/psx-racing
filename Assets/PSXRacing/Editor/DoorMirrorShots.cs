using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;
using Debug = UnityEngine.Debug;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The door mirrors from the seat, before and after (owner, 2026-10-19:
    /// "I should see the mirrors of cars from cockpit windows", "door mirrors
    /// should be functional like rear-view mirror"). One venue
    /// (PSX_SHOT_VENUE, else BeechGap: a built road to see in the glass),
    /// noon, the shells in PSX_MIRROR_SHELLS (else a 90s coupe, a muscle car,
    /// a hatch and one with black housings), left-hand drive. Per shell,
    /// three looks - ahead, over to the driver's door, over to the
    /// passenger's - each BEFORE (the old cut: the whole body box behind the
    /// windscreen, no live glass) and AFTER. Then each mirror's own picture,
    /// and the frame cost: a door mirror's render against the driver's and
    /// the rear-view's. Last, the glass 5 km from the origin
    /// (PSX_MIRROR_FIGHT_VENUE, else UptownLoop), where a layer a millimetre
    /// off another fights: the car and the eye stepped 0.16 mm at a time, and
    /// the share of the glass's pixels that change.
    /// Writes Screenshots/DoorMirrors/&lt;shell&gt;_&lt;look&gt;_&lt;before|after&gt;.png,
    /// &lt;shell&gt;_glass_&lt;L|R&gt;.png and timing.txt.
    /// </summary>
    public static class DoorMirrorShots
    {
        [MenuItem("PSX Racing/Capture Door Mirror Views")]
        public static void Capture()
        {
            var def = TrackCatalog.At(0);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;

            string root = Path.GetDirectoryName(Application.dataPath);
            string dir = Path.Combine(root, "Screenshots", "DoorMirrors");
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
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            if (car != null) { car.currentRPM = 3000f; car.speedKmh = 70f; }

            string shells = System.Environment.GetEnvironmentVariable("PSX_MIRROR_SHELLS");
            if (string.IsNullOrEmpty(shells)) shells = "rx7_fd_93,charger_70,civic_eg,hatch_83_popup";
            const float Max = CockpitLook.MaxYaw;
            var timing = new System.Text.StringBuilder();
            CockpitInterior ci = null;
            bool timed = false;
            foreach (var key0 in shells.Split(','))
            {
                string key = key0.Trim();
                var shell = CarModelLibrary.Load(key);
                if (shell == null || body == null || shell.key != key) { Debug.LogError("[DoorMirror] no shell " + key); continue; }
                body.Apply(shell, 0);
                Physics.SyncTransforms();
                Vector3 c = box.center, s = box.size;
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[DoorMirror] no CockpitInterior on the player"); break; }
                ci.Refit();
                ci.Show(true, true);
                ci.SetSteer(0f, 34f);
                ci.Tick(0f, null);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();
                var doors = ci.Doors;
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell, body.LengthScale, body.BodyLift, false);
                Debug.Log($"[DoorMirror] {key}: mirrors {(doors != null ? doors.Count : 0)} cut z {ci.Current.cutZ:0.000} " +
                          $"cabin half-width {ci.Current.cabinHalfW:0.000} tail {ci.Current.cabinTailZ:0.000} scale {ci.Current.scale:0.000} " +
                          $"eye {eye.x:0.000},{eye.y:0.000},{eye.z:0.000}");

                // The mirrors' own pictures (they render from outside, shell whole).
                ci.PreviewDoorMirrors(cam);
                if (doors != null)
                    for (int i = 0; i < doors.Count; i++)
                    {
                        var dc = doors.CameraOf(i);
                        if (dc != null && dc.targetTexture != null)
                            SavePng(dc.targetTexture, Path.Combine(dir, $"{key}_glass_{(doors.SignOf(i) < 0f ? "L" : "R")}.png"), true);
                    }

                // (name, look yaw: + toward the passenger)
                var looks = new (string, float)[] { ("ahead", 0f), ("driverside", -38f), ("passside", 40f) };
                foreach (var (look, yaw0) in looks)
                {
                    float yaw = yaw0 * CockpitLook.PassengerSign;
                    var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                    cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                    cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / Max);
                    foreach (bool after in new[] { false, true })
                    {
                        if (doors != null) doors.ShowGlass(after);
                        ci.HideShell(true);
                        // BEFORE: the cut as it was - the whole body box behind
                        // the windscreen's base, nothing kept.
                        if (!after) CabinClip.Open(t, ci.Current.cutZ, c, s);
                        if (doors != null) doors.Pose();
                        PSXScreenshotTool.ShotAs(cam, $"DoorMirrors/{key}_{look}_{(after ? "after" : "before")}", t.TransformPoint(eye), rot);
                        ci.HideShell(false);
                    }
                }
                if (doors != null) doors.ShowGlass(true);

                if (!timed && doors != null && doors.Count > 0)
                {
                    timed = true;
                    Time(ci, doors, cam, t, eye, timing);
                }
                ci.Show(false, false);
                if (cv != null) cv.PreviewShow(false);
            }
            DriveSide.Override = null;
            PSXScreenshotTool.AspectOverride = 0f;
            ChaseCamera.PreviewView(keepView);
            cam.nearClipPlane = keepNear;
            cam.fieldOfView = keepFov;
            Debug.Log("[DoorMirror] shots of " + def.id + " written to " + dir);

            string fv = System.Environment.GetEnvironmentVariable("PSX_MIRROR_FIGHT_VENUE");
            if (string.IsNullOrEmpty(fv)) fv = "UptownLoop";
            Fight(fv, shells.Split(',')[0].Trim(), dir, timing);
            File.WriteAllText(Path.Combine(dir, "timing.txt"), timing.ToString());
            Debug.Log("[DoorMirror] " + timing);
        }

        /// <summary>The glass on its housing 5 km from the origin: the car
        /// and the eye moved together in eight 0.16 mm steps, the frame
        /// rendered each time, and the pixels of the left glass that change
        /// against the first (a fight shows as a pattern trading places).</summary>
        static void Fight(string venueId, string key, string dir, System.Text.StringBuilder log)
        {
            var def = TrackCatalog.At(0);
            foreach (var d in TrackCatalog.All) if (d.id == venueId) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) return;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }
            var t = player.transform;
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            var shell = CarModelLibrary.Load(key);
            if (shell == null || body == null || box == null) return;
            DriveSide.Override = false;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            body.Apply(shell, 0);
            Physics.SyncTransforms();
            if (cv != null) cv.PreviewShow(true);
            var ci = player.GetComponentInChildren<CockpitInterior>(true);
            if (ci == null) { DriveSide.Override = null; return; }
            ci.Refit();
            ci.Show(true, true);
            ci.Tick(0f, null);
            var doors = ci.Doors;
            if (doors == null) { log.Append($"fight: {key} has no live mirrors\n"); DriveSide.Override = null; return; }
            ci.PreviewDoorMirrors(cam);
            Vector3 c = box.center, s = box.size;
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell, body.LengthScale, body.BodyLift, false);
            float yaw = -38f * CockpitLook.PassengerSign;
            cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
            cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, yaw * CockpitLook.PassengerSign / CockpitLook.MaxYaw);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var tex = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            Color32[] first = null;
            int changed = 0, inRect = 0;
            const int Steps = 8;
            Vector3 start = t.position;
            Rect r = default;
            for (int k = 0; k < Steps; k++)
            {
                t.position = start + new Vector3(0.00013f, 0f, 0.00009f) * k;
                Physics.SyncTransforms();
                ci.HideShell(true);
                doors.Pose();
                var rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(yaw), yaw, 0f);
                cam.transform.SetPositionAndRotation(t.TransformPoint(eye), rot);
                if (k == 0) r = GlassRect(doors, cam, rt.width, rt.height);
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                ci.HideShell(false);
                var keep = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = keep;
                var px = tex.GetPixels32();
                if (k == 0) { first = px; continue; }
                for (int y = (int)r.yMin; y < (int)r.yMax; y++)
                    for (int x = (int)r.xMin; x < (int)r.xMax; x++)
                    {
                        int i = y * rt.width + x;
                        inRect++;
                        Color32 a = first[i], b = px[i];
                        if (Mathf.Abs(a.r - b.r) > 8 || Mathf.Abs(a.g - b.g) > 8 || Mathf.Abs(a.b - b.b) > 8) changed++;
                    }
            }
            t.position = start;
            Physics.SyncTransforms();
            File.WriteAllBytes(Path.Combine(dir, $"fight_{key}.png"), tex.EncodeToPNG());
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            log.Append($"GLASS FIGHT {venueId} {key} (car {start.magnitude:0} m from the origin): " +
                       $"{(inRect > 0 ? 100f * changed / inRect : 0f):0.00}% of {inRect / (Steps - 1)} glass pixels change over {Steps} steps of 0.16 mm " +
                       $"(rect {r.xMin:0},{r.yMin:0} {r.width:0}x{r.height:0})\n");
            DriveSide.Override = null;
        }

        /// <summary>The left glass's box on the frame, inset two pixels.</summary>
        static Rect GlassRect(DoorMirrorRig doors, Camera cam, int w, int h)
        {
            var b = doors.GlassBounds(0);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                var v = cam.WorldToViewportPoint(p);
                x0 = Mathf.Min(x0, v.x * w); x1 = Mathf.Max(x1, v.x * w); y0 = Mathf.Min(y0, v.y * h); y1 = Mathf.Max(y1, v.y * h);
            }
            x0 = Mathf.Clamp(x0 + 2f, 0f, w); x1 = Mathf.Clamp(x1 - 2f, 0f, w); y0 = Mathf.Clamp(y0 + 2f, 0f, h); y1 = Mathf.Clamp(y1 - 2f, 0f, h);
            return Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }

        /// <summary>Milliseconds a render, CPU and GPU (a one-pixel read
        /// after the batch waits for the GPU): a door mirror, the rear-view
        /// mirror, and the driver's own frame at 1280x720.</summary>
        static void Time(CockpitInterior ci, DoorMirrorRig doors, Camera cam, Transform t, Vector3 eye, System.Text.StringBuilder log)
        {
            const int N = 40;
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            float Batch(Camera c, int n, System.Action each)
            {
                var keep = RenderTexture.active;
                for (int i = 0; i < 3; i++) { each?.Invoke(); c.Render(); }
                Sync(c.targetTexture, probe);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) { each?.Invoke(); c.Render(); }
                Sync(c.targetTexture, probe);
                sw.Stop();
                RenderTexture.active = keep;
                return (float)sw.Elapsed.TotalMilliseconds / n;
            }

            ci.PreviewDoorMirrors(cam);
            var dc = doors.CameraOf(0);
            float door = dc != null ? Batch(dc, N, null) : -1f;

            ci.PreviewMirror(cam);
            Camera rear = null;
            foreach (var c in ci.GetComponentsInChildren<Camera>(true)) if (c.name == "CockpitMirrorCamera") rear = c;
            float rv = -1f;
            if (rear != null)
            {
                ci.HideShell(true);
                rv = Batch(rear, N, null);
                ci.HideShell(false);
            }

            // The driver's frame, into a 1280x720 target.
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var keepT = cam.targetTexture;
            var keepPos = cam.transform.position; var keepRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(t.TransformPoint(eye), t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f));
            cam.targetTexture = rt;
            ci.HideShell(true);
            float drv = Batch(cam, 10, null);
            ci.HideShell(false);
            cam.targetTexture = keepT;
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(probe);

            string rtSize = dc != null && dc.targetTexture != null ? $"{dc.targetTexture.width}x{dc.targetTexture.height}" : "?";
            log.Append($"editor -force-glcore, one render each, CPU+GPU (1-px read after the batch):\n");
            log.Append($"  door mirror ({rtSize}): {door:0.00} ms\n");
            log.Append($"  rear-view mirror (256x64): {rv:0.00} ms\n");
            log.Append($"  driver frame (1280x720): {drv:0.00} ms\n");
            log.Append($"  schedule: door mirrors only when on screen, on the frames the rear-view skips (odd); two in view take turns.\n");
            log.Append($"  per-frame average with one mirror in view: +{door * 0.5f:0.00} ms (desktop), phones +{door * 0.25f:0.00} ms\n");
        }

        static void Sync(RenderTexture rt, Texture2D probe)
        {
            if (rt == null) { GL.Flush(); return; }
            var keep = RenderTexture.active;
            RenderTexture.active = rt;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            probe.Apply(false);
            RenderTexture.active = keep;
        }

        /// <summary>A render texture to PNG, flipped across as the glass shows
        /// it (`asGlass`), blown up 3x nearest.</summary>
        static void SavePng(RenderTexture rt, string path, bool asGlass)
        {
            var keep = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = keep;
            const int K = 3;
            var big = new Texture2D(rt.width * K, rt.height * K, TextureFormat.RGB24, false);
            for (int y = 0; y < big.height; y++)
                for (int x = 0; x < big.width; x++)
                {
                    int sx = x / K;
                    if (asGlass) sx = rt.width - 1 - sx;
                    big.SetPixel(x, y, tex.GetPixel(sx, y / K));
                }
            big.Apply(false);
            File.WriteAllBytes(path, big.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(big);
        }
    }
}
