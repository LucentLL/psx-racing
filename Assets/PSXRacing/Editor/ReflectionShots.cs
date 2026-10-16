using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The world-in-the-paint check (CarReflections, 2026-10-12): the grid car
    /// from several angles, once with the paint on the sky lookup alone and
    /// once with the six-view panorama, written side by side as
    /// Screenshots\refl_&lt;venue&gt;_&lt;hour&gt;_&lt;view&gt;_off|on.png (+ _pano).
    /// PSX_REFL_VENUES=BlueRidge,CityCircuit picks the venues;
    /// PSX_REFL_HOUR=noon,sunset (one or several) the hours;
    /// PSX_REFL_VIEWS=front34,side,rear34,graze (default) or chase,rear34c;
    /// PSX_REFL_CAR=viper_gts (+ PSX_REFL_SKIN=0) dresses the grid car;
    /// PSX_REFL_WP=n puts it on waypoint n, PSX_REFL_WP=auto on the straightest
    /// run with a wall on its left (the owner's Linn Cove frame, 2026-10-15);
    /// PSX_REFL_DEBUG=4,6 also writes the paint's debug terms (_dbgN) for the
    /// "on" frames; PSX_REFL_TIME=1 logs the cost of a full refresh per face size.
    /// Headless: -executeMethod PSXRacing.EditorTools.ReflectionShots.Run
    /// </summary>
    public static class ReflectionShots
    {
        static string Env(string k, string d)
        {
            string v = System.Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? d : v;
        }

        public static void Run()
        {
            string venues = Env("PSX_REFL_VENUES", "BlueRidge,CityCircuit");
            string[] hours = Env("PSX_REFL_HOUR", "noon").Split(',');
            string[] viewNames = Env("PSX_REFL_VIEWS", "front34,side,rear34,graze").Split(',');
            string carKey = Env("PSX_REFL_CAR", "");
            int skin = int.TryParse(Env("PSX_REFL_SKIN", "0"), out int sk) ? sk : 0;
            string wpArg = Env("PSX_REFL_WP", "");
            var dbg = new System.Collections.Generic.List<int>();
            foreach (var s in Env("PSX_REFL_DEBUG", "").Split(','))
                if (int.TryParse(s.Trim(), out int m)) dbg.Add(m);
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);

            foreach (var id in venues.Split(','))
            {
                var def = System.Array.Find(TrackCatalog.All, d => d.id == id.Trim());
                if (def == null) { Debug.LogWarning("[ReflShot] no venue " + id); continue; }
                foreach (var hourRaw in hours)
                {
                    string hourName = hourRaw.Trim().ToLowerInvariant();
                    int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName, System.StringComparison.OrdinalIgnoreCase));
                    if (hour < 0) hour = TimeOfDay.Noon;
                    if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogWarning("[ReflShot] " + id + " did not open"); continue; }
                    var t = player.transform;
                    if (carKey.Length > 0)
                    {
                        var body = player.GetComponentInChildren<CarBody>(true);
                        var cdef = CarModelLibrary.Load(carKey);
                        if (body != null && cdef != null) body.Apply(cdef, Mathf.Clamp(skin, 0, cdef.skinNames.Length - 1));
                        else Debug.LogWarning("[ReflShot] could not dress " + carKey);
                    }
                    if (wpArg.Length > 0) Place(t, wpArg);
                    var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                    TimeOfDay.Apply(hour, sun);
                    var globals = Object.FindFirstObjectByType<PSXGlobals>();
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                    Vector3 aim = t.position + Vector3.up * 0.7f;
                    float fov0 = cam.fieldOfView;
                    foreach (var vnRaw in viewNames)
                    {
                        string vn = vnRaw.Trim();
                        Vector3 eye; Quaternion rot; float fov = fov0;
                        switch (vn)
                        {
                            case "chase":
                                ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, 40f, t, ChaseCamera.FrameOf(player),
                                                       ChaseCamera.LiveDials(), out eye, out rot, out fov, out _);
                                break;
                            case "rear34c": eye = t.TransformPoint(new Vector3(-2.3f, 1.55f, -3.9f)); rot = Quaternion.LookRotation(t.position + Vector3.up * 0.9f - eye); break;
                            case "front34": eye = t.TransformPoint(new Vector3(3.2f, 1.5f, 5.0f)); rot = Quaternion.LookRotation(aim - eye); break;
                            case "side": eye = t.TransformPoint(new Vector3(5.0f, 1.2f, 0.4f)); rot = Quaternion.LookRotation(aim - eye); break;
                            case "rear34": eye = t.TransformPoint(new Vector3(-3.0f, 2.2f, -5.2f)); rot = Quaternion.LookRotation(aim - eye); break;
                            case "graze": eye = t.TransformPoint(new Vector3(2.4f, 0.9f, 4.2f)); rot = Quaternion.LookRotation(aim - eye); break;
                            default: Debug.LogWarning("[ReflShot] no view " + vn); continue;
                        }
                        cam.fieldOfView = fov;
                        string stem = $"refl_{def.id}_{hourName}_{vn}";
                        CarReflections.Off();
                        PSXScreenshotTool.ShotAs(cam, stem + "_off", eye, rot);
                        if (!CarReflections.CaptureNow(t, cam)) { Debug.LogError("[ReflShot] EnvPano shader missing"); return; }
                        PSXScreenshotTool.ShotAs(cam, stem + "_on", eye, rot);
                        foreach (int m in dbg)
                        {
                            Shader.SetGlobalFloat("_PSXPaintDebug", m);
                            PSXScreenshotTool.ShotAs(cam, stem + "_on_dbg" + m, eye, rot);
                            Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                        }
                        cam.fieldOfView = fov0;
                    }
                    // the panorama itself, for checking what the paint reads
                    var pano = CarReflections.Panorama;
                    if (pano != null) WritePano(pano, Path.Combine(dir, $"refl_{def.id}_{hourName}_pano.png"));
                    if (Env("PSX_REFL_TIME", "") == "1") Time(t, cam);
                    CarReflections.Off();
                    Debug.Log("[ReflShot] " + def.id + " " + hourName + " done");
                }
            }
        }

        /// <summary>The panorama as the paint reads it (decoded when it is
        /// stored encoded), for checking what is in the lacquer.</summary>
        static void WritePano(RenderTexture pano, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = pano;
            var tex = new Texture2D(pano.width, pano.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, pano.width, pano.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>The car on waypoint <paramref name="arg"/> (or "auto": the
        /// straightest run with a wall on its left and none on its right),
        /// in the right-hand lane, facing down the road.</summary>
        static void Place(Transform car, string arg)
        {
            var tp = Object.FindFirstObjectByType<TrackPath>();
            if (tp == null || tp.waypoints == null || tp.waypoints.Length < 40) return;
            var w = tp.waypoints;
            int n = w.Length, pick = -1;
            if (arg != "auto") int.TryParse(arg, out pick);
            if (pick < 0)
            {
                float best = float.MinValue;
                Physics.SyncTransforms();
                for (int i = 20; i < n - 20; i += 3)
                {
                    float bend = 0f;
                    for (int j = i - 12; j <= i + 12; j++) bend = Mathf.Max(bend, Mathf.Abs(tp.curvatures != null && j < tp.curvatures.Length ? tp.curvatures[j] : 0f));
                    if (bend > 1f / 350f) continue;
                    int nL = 0, nR = 0;
                    for (int j = i - 4; j <= i + 12; j += 2)
                    {
                        Vector3 f = (w[j + 1] - w[j]); f.y = 0f; f.Normalize();
                        Vector3 r = Vector3.Cross(Vector3.up, f);
                        Vector3 o = w[j] + Vector3.up * 0.8f;
                        if (Physics.Raycast(o, -r, out var hl, tp.roadWidth * 0.5f + 5f) && hl.collider.name.Contains("Wall")) nL++;
                        if (Physics.Raycast(o, r, out var hr, tp.roadWidth * 0.5f + 5f) && hr.collider.name.Contains("Wall")) nR++;
                    }
                    float score = nL * 2f - nR * 3f - bend * 2000f;
                    if (score > best) { best = score; pick = i; }
                }
                Debug.Log("[ReflShot] auto spot wp " + pick + " score " + best.ToString("0.0"));
            }
            pick = Mathf.Clamp(pick, 0, n - 2);
            Vector3 fwd = w[pick + 1] - w[pick]; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 pos = w[pick] + right * (tp.roadWidth * 0.25f);
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var g, 8f)) pos.y = g.point.y;
            car.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
            Physics.SyncTransforms();
            Debug.Log("[ReflShot] car on wp " + pick + " at " + pos);
        }

        /// <summary>What a full refresh of the six faces + the stitch costs
        /// here, per face size: the CPU submit and the GPU, waited on by a
        /// one-pixel read back. The editor is not the browser; the RATIO
        /// between sizes is the number to carry.</summary>
        static void Time(Transform car, Camera cam)
        {
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            foreach (int res in new[] { 64, 128, 256 })
            {
                CarReflections.FaceResOverride = res;
                CarReflections.CaptureNow(car, cam);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int reps = 20;
                for (int k = 0; k < reps; k++) CarReflections.CaptureNow(car, cam);
                var prev = RenderTexture.active;
                RenderTexture.active = CarReflections.Panorama;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = prev;
                sw.Stop();
                Debug.Log($"[ReflShot] TIME faces {res}px: {sw.Elapsed.TotalMilliseconds / reps:0.00} ms per 6-face refresh (editor)");
            }
            CarReflections.FaceResOverride = 0;
            Object.DestroyImmediate(one);
        }
    }
}
