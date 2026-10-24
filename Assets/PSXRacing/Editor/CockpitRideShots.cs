using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CABIN RIDES THE BODY, checked (owner, 2026-10-24: "car body moves
    /// separately from the cockpit, leaving gaps between door and hood.
    /// Rearview mirrors also shows floating disconnecting spoilers and trunks
    /// (should not show any body, just rear view)"). One venue
    /// (PSX_SHOT_VENUE, else BeechGap) at noon; per shell (PSX_RIDE_SHELLS,
    /// else the R34 and the Charger) the cockpit view with the body at rest
    /// and leaned both ways (roll, pitch and a rise off its tyres, as
    /// CarBodyLean and the bump stops pose it), the cabin and the eye on the
    /// car's frame (OLD) and riding the body (NEW); and the rear-view
    /// mirror's picture with the old cut and the new.
    /// Writes Screenshots/ride_&lt;shell&gt;_&lt;old|new&gt;_&lt;state&gt;.png and
    /// ride_&lt;shell&gt;_mirror_&lt;old|new&gt;.png; logs how far the dash moved on
    /// screen between rest and each lean ("[Ride]" lines).
    /// </summary>
    public static class CockpitRideShots
    {
        [MenuItem("PSX Racing/Capture Cockpit Ride")]
        public static void Capture()
        {
            var def = TrackCatalog.At(0);
            string only = System.Environment.GetEnvironmentVariable("PSX_SHOT_VENUE");
            if (string.IsNullOrEmpty(only)) only = "BeechGap";
            foreach (var d in TrackCatalog.All) if (d.id == only) { def = d; break; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[Ride] venue did not open"); return; }

            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 540;
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null && globals.sun != null) { TimeOfDay.Apply(TimeOfDay.Noon, globals.sun); globals.Apply(); }

            var t = player.transform;
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            PSXScreenshotTool.AspectOverride = 16f / 9f;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);

            string shells = System.Environment.GetEnvironmentVariable("PSX_RIDE_SHELLS");
            if (string.IsNullOrEmpty(shells)) shells = "gtr_99,charger_69";
            // roll, pitch (deg), rise (m): a hard corner on the brakes over a
            // bump, and the other way off it
            var states = new (string name, float roll, float pitch, float rise)[]
            {
                ("rest", 0f, 0f, 0f), ("leanA", 1.7f, 1.2f, 0.03f), ("leanB", -1.7f, -1.2f, -0.02f),
            };
            CockpitInterior ci = null;
            foreach (var key0 in shells.Split(','))
            {
                string key = key0.Trim();
                var shell = CarModelLibrary.Load(key);
                if (shell == null || body == null) { Debug.LogError("[Ride] no shell " + key); continue; }
                body.Apply(shell, 0);
                var br = body.bodyRoot;
                if (br == null) { Debug.LogError("[Ride] no body root"); continue; }
                Vector3 c = box.center, s = box.size;
                if (cv != null) cv.PreviewShow(true);
                if (ci == null) ci = player.GetComponentInChildren<CockpitInterior>(true);
                if (ci == null) { Debug.LogError("[Ride] no CockpitInterior on the player"); break; }
                ci.Refit();
                ci.Show(true, true);
                ci.SetSteer(0f, 34f);
                ci.Tick(0f, null);
                foreach (var gc in Object.FindObjectsByType<GaugeCluster>(FindObjectsSortMode.None)) gc.Build();
                Canvas.ForceUpdateCanvases();
                Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, shell, body.LengthScale, body.BodyLift, DriveSide.IsRHD(player.GetComponent<CarController>()));
                var eyeRot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit), 0f, 0f);
                cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
                cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale);
                Vector3 restPos = body.SeatPosition;
                Quaternion restRot = Quaternion.Euler(0f, shell.bodyYaw, 0f);

                foreach (bool ride in new[] { false, true })
                {
                    Texture2D rest = null;
                    foreach (var st in states)
                    {
                        br.localPosition = restPos + Vector3.up * st.rise;
                        br.localRotation = Quaternion.Euler(st.pitch, 0f, st.roll) * restRot;
                        Vector3 p = t.TransformPoint(eye);
                        Quaternion q = eyeRot;
                        ci.PreviewRide(ref p, ref q, ride);
                        ci.HideShell(true);
                        string name = $"ride_{key}_{(ride ? "new" : "old")}_{st.name}";
                        PSXScreenshotTool.ShotAs(cam, name, p, q);
                        ci.HideShell(false);
                        var img = Load(Path.Combine(dir, name + ".png"));
                        if (st.name == "rest") rest = img;
                        else if (rest != null && img != null)
                        {
                            Debug.Log($"[Ride] {key} {(ride ? "NEW" : "OLD")} {st.name}: dash moved {DashShift(rest, img):0.0} px, bonnet+dash pixels changed {Changed(rest, img, 0.42f, 0.70f) * 100f:0.0}% (lower band)");
                            Object.DestroyImmediate(img);
                        }
                    }
                    if (rest != null) Object.DestroyImmediate(rest);
                }
                br.localPosition = restPos;
                br.localRotation = restRot;
                {
                    Vector3 p = t.TransformPoint(eye); Quaternion q = eyeRot;
                    ci.PreviewRide(ref p, ref q, true);
                    cam.transform.SetPositionAndRotation(p, q);
                }

                foreach (bool whole in new[] { false, true })
                {
                    ci.PreviewMirror(cam, whole);
                    var rt = ci.MirrorTexture;
                    if (rt == null) { Debug.LogError("[Ride] no mirror texture"); break; }
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"ride_{key}_mirror_{(whole ? "new" : "old")}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                ci.Show(false, false);
                if (cv != null) cv.PreviewShow(false);
                Debug.Log($"[Ride] {key}: shots written");
            }
            PSXScreenshotTool.AspectOverride = 0f;
            Debug.Log("[Ride] DONE");
        }

        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        /// <summary>The share of pixels in a horizontal band (fractions of the
        /// height from the bottom) that changed visibly.</summary>
        static float Changed(Texture2D a, Texture2D b, float lo, float hi)
        {
            if (a.width != b.width || a.height != b.height) return 1f;
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            int w = a.width, y0 = Mathf.RoundToInt(a.height * lo), y1 = Mathf.RoundToInt(a.height * hi), n = 0, d = 0;
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; n++;
                    if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 36) d++;
                }
            return n > 0 ? d / (float)n : 0f;
        }

        /// <summary>How far (px) the bottom third - the dash - moved between
        /// two frames: the vertical shift that best lines its rows up.</summary>
        static float DashShift(Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height) return -1f;
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            int w = a.width, h = a.height;
            float Row(Color32[] p, int y) { float s = 0f; for (int x = 0; x < w; x += 2) { var c = p[y * w + x]; s += c.r + c.g + c.b; } return s; }
            int y0 = h / 10, y1 = h / 3, best = 0; float bestErr = float.MaxValue;
            for (int k = -24; k <= 24; k++)
            {
                float e = 0f;
                for (int y = y0; y < y1; y++) { int yb = y + k; if (yb < 0 || yb >= h) { e += 1e6f; continue; } e += Mathf.Abs(Row(pa, y) - Row(pb, yb)); }
                if (e < bestErr) { bestErr = e; best = k; }
            }
            return Mathf.Abs(best);
        }
    }
}
