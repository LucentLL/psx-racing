using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// IS THE GPU-DRIVEN FOREST THE SAME FOREST (MeshForest.Gpu.cs, 2026-11-09)?
    /// The same eye, the same hour and dress, drawn three ways from one scene:
    ///
    ///   cpu   the old path (re-bucket on the main thread, RenderMeshInstanced)
    ///   gpu   the GPU path with the chunk pass's wide cone for the plants (what
    ///         every edit-mode shot tool draws)
    ///   gpuv  the GPU path with the PLAYER's cull: each plant against the view
    ///         frustum itself
    ///
    /// at stations PSX_FP_M along the road, views PSX_FP_VIEWS (chase: the
    /// game's camera; vista: 25 m up, turned to the open side; rev: the road
    /// the other way), and a SEQUENCE along the road (PSX_FP_SEQ=from:to:step
    /// metres; cpu and gpuv at every step) for the level hand-overs. Writes
    /// Screenshots\fp_*.png and fp_probe.txt; scratch tools difference them.
    /// </summary>
    public static class ForestGpuProbe
    {
        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Capture()
        {
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "fp_*")) File.Delete(f);
            var log = new System.Text.StringBuilder();

            string id = Env("PSX_FP_VENUE", "SwissNC226A");
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { Debug.LogError("[ForestGpuProbe] no scene " + id); return; }

            var path = Object.FindFirstObjectByType<TrackPath>();
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            var dressing = Object.FindFirstObjectByType<SeasonDress>();
            var forest = Object.FindFirstObjectByType<MeshForest>();
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (path == null || globals == null || sun == null || forest == null) { Debug.LogError("[ForestGpuProbe] scene parts missing (mesh forest " + (forest != null) + ")"); return; }
            if (output != null) output.height = int.Parse(Env("PSX_FP_HEIGHT", "720"), CultureInfo.InvariantCulture);

            var sc = cam.GetComponent<StageCulling>();
            if (sc != null) sc.Configure(cam);
            cam.aspect = 16f / 9f;      // the shot's (PSXScreenshotTool.ShotWidth): the player's cull needs the frustum it will be drawn with

            int day = int.Parse(Env("PSX_FP_DAY", "50"), CultureInfo.InvariantCulture);
            int keepDay = RaceHandoff.CalendarDay;
            RaceHandoff.CalendarDay = day;
            int dress = (int)Seasons.Of(day);
            string dressName = Env("PSX_FP_DRESS", "");
            if (dressName.Length > 0) dress = dressName.ToLower() == "snow" ? Seasons.DressSnow : (int)(Season)System.Enum.Parse(typeof(Season), dressName, true);
            if (dressing != null)
            {
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                dressing.Apply(dress);
            }
            log.AppendLine($"venue {id}, day {day}, dress {Seasons.DressNames[dress]}, camera far {cam.farClipPlane:0} m, fov {cam.fieldOfView:0}, compute shaders {SystemInfo.supportsComputeShaders}, device {SystemInfo.graphicsDeviceType}");

            bool keepDyn = SkyModePrefs.Dynamic;
            int keepWeather = RaceHandoff.WeatherOverride;
            bool keepGpu = MeshForest.GpuDriven;
            try
            {
                SkyModePrefs.Dynamic = Env("PSX_FP_DYNSKY", "0") == "1";
                RaceHandoff.WeatherOverride = -1;
                Seasons.Forced = null; Seasons.Invalidate();
                var w = Seasons.Now;
                w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear;
                Seasons.Forced = w;

                int hour = -1;
                string hourName = Env("PSX_FP_HOUR", "noon").Trim().ToLower();
                for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == hourName) hour = h;
                if (hour < 0) hour = 0;
                TimeOfDay.Apply(hour, sun);
                globals.Apply();

                var counts = new System.Collections.Generic.Dictionary<string, string>();
                bool saidVariants = false;
                void Shoot(string name, Vector3 eye, Quaternion rot, string mode)
                {
                    if (!saidVariants) { saidVariants = true; forest.DrawNow(cam); log.AppendLine("species:\n" + forest.DebugVariants()); }
                    cam.transform.SetPositionAndRotation(eye, rot);
                    if (SkyModePrefs.Dynamic) DynamicSky.PrimeClouds(eye);
                    // the player's order: the forest sets which chunks show their
                    // cards, THEN the sun's shadow map is drawn for this eye (the
                    // hidden cards its stand-in casters), then the view
                    // (gpux: the GPU cull with the CPU path's own margin and no
                    // frustum or shrunk-away test - list for list the same plants)
                    // (a trailing n: no sun or sky shadow at all; s: no sun shadow; k: no sky shade -
                    // which map is a difference in?)
                    string shadeOff = mode.EndsWith("n") || mode.EndsWith("s") || mode.EndsWith("k") ? mode.Substring(mode.Length - 1) : "";
                    bool noShade = shadeOff.Length > 0;
                    var sStr = typeof(SunShadows).GetProperty("Strength"); var sSky = typeof(SunShadows).GetProperty("SkyStrength");
                    float keepStr = SunShadows.Strength, keepSky = SunShadows.SkyStrength;
                    if (shadeOff == "n" || shadeOff == "s") sStr.SetValue(null, 0f);
                    if (shadeOff == "n" || shadeOff == "k") sSky.SetValue(null, 0f);
                    if (noShade) mode = mode.Substring(0, mode.Length - 1);
                    MeshForest.GpuDriven = !mode.StartsWith("cpu");
                    MeshForest.DebugExact = mode == "gpux";
                    forest.DrawNow(cam, mode == "gpuv");
                    SunShadows.RenderFor(cam);
                    forest.DrawNow(cam, mode == "gpuv");
                    if (noShade) mode += shadeOff;
                    log.AppendLine("  " + name + "_" + mode + ": " + forest.DebugCards());
                    if (mode == "cpu" || mode == "gpux") counts[mode] = forest.DebugCounts();
                    if (mode == "gpux" && counts.ContainsKey("cpu"))
                    {
                        var a = counts["cpu"].Split('\n'); var b = counts["gpux"].Split('\n');
                        int bad = 0;
                        for (int k = 0; k < Mathf.Max(a.Length, b.Length); k++)
                        {
                            string la = k < a.Length ? a[k] : "", lb = k < b.Length ? b[k] : "";
                            if (la != lb) { bad++; if (bad <= 12) log.AppendLine($"  {name} LISTS DIFFER: cpu {la} | gpu {lb}"); }
                        }
                        log.AppendLine($"  {name}: plants per species and level, CPU lists against the GPU cull with the same rule: {(bad == 0 ? "identical" : bad + " species differ")} ({a.Length - 1} species)");
                    }
                    PSXScreenshotTool.ShotAs(cam, name + "_" + mode, eye, rot);
                    MeshForest.DebugExact = false;
                    if (noShade) { sStr.SetValue(null, keepStr); sSky.SetValue(null, keepSky); }
                    // PSX_FP_DUMPSHADOW=1: the sun's and the sky's maps as this shot read them
                    if (Env("PSX_FP_DUMPSHADOW", "0") == "1")
                    {
                        foreach (string g in new[] { "_PSXShadowMap", "_PSXSkyMap" })
                        {
                            if (!(Shader.GetGlobalTexture(g) is RenderTexture sm)) continue;
                            var prev = RenderTexture.active;
                            RenderTexture.active = sm;
                            var tex = new Texture2D(sm.width, sm.height, TextureFormat.RGBA32, false, true);
                            tex.ReadPixels(new Rect(0, 0, sm.width, sm.height), 0, 0);
                            tex.Apply();
                            RenderTexture.active = prev;
                            File.WriteAllBytes(Path.Combine(outDir, name + "_" + mode + g + ".png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                        log.AppendLine($"  {name}_{mode}: casters drawn {SunShadows.DrawnLast}, calls {SunShadows.DrawCallsLast}; shadow params {Shader.GetGlobalVector("_PSXShadowParams")} sky params {Shader.GetGlobalVector("_PSXSkyParams")} car params {Shader.GetGlobalVector("_PSXCarShadowParams")}");
                        log.AppendLine($"    shadow matrix {Shader.GetGlobalMatrix("_PSXShadowMatrix").ToString("0.000000").Replace("\n", " | ")}");
                        log.AppendLine($"    sky matrix {Shader.GetGlobalMatrix("_PSXSkyMatrix").ToString("0.000000").Replace("\n", " | ")}");
                    }
                }

                string[] views = Env("PSX_FP_VIEWS", "chase,vista,rev").Split(',');
                foreach (string ms in Env("PSX_FP_M", "7900,8400,9000,9600").Split(','))
                {
                    if (ms.Trim().Length == 0) continue;
                    float m = float.Parse(ms, CultureInfo.InvariantCulture);
                    int at = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 2, path.Count - 12);
                    Vector3 p = path.GetPoint(at);
                    Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
                    foreach (string view in views)
                    {
                        Vector3 eye; Quaternion rot;
                        if (view == "vista")
                        {
                            eye = p + Vector3.up * 25f;
                            rot = Quaternion.LookRotation(Quaternion.Euler(0f, OpenSide(p, fwd) * 60f, 0f) * fwd);
                        }
                        else
                        {
                            Vector3 f = view == "rev" ? -fwd : fwd;
                            eye = p - f * 8f + Vector3.up * 2.6f;
                            rot = Quaternion.LookRotation(p + Vector3.up * 0.8f + f * 20f - eye);
                        }
                        string name = $"fp_{hourName}_{ms.Trim()}_{view}";
                        foreach (string mode in Env("PSX_FP_MODES", "cpu,gpux,gpu,gpuv,cpu2").Split(',')) Shoot(name, eye, rot, mode);
                        log.AppendLine($"{name}: eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) yaw {rot.eulerAngles.y:0.0} pitch {rot.eulerAngles.x:0.0}");
                    }
                }

                // THE SEQUENCE: the chase camera down the road in small steps
                var seq = Env("PSX_FP_SEQ", "").Split(':');
                if (seq.Length == 3)
                {
                    float from = float.Parse(seq[0], CultureInfo.InvariantCulture), to = float.Parse(seq[1], CultureInfo.InvariantCulture),
                          step = Mathf.Max(0.5f, float.Parse(seq[2], CultureInfo.InvariantCulture));
                    int k = 0;
                    for (float m = from; m <= to + 0.01f; m += step, k++)
                    {
                        // between the path's points, so the eye moves evenly
                        float fi = m / TrackCatalog.Spacing;
                        int i0 = Mathf.Clamp(Mathf.FloorToInt(fi), 2, path.Count - 13);
                        float t = Mathf.Clamp01(fi - i0);
                        Vector3 p = Vector3.Lerp(path.GetPoint(i0), path.GetPoint(i0 + 1), t);
                        Vector3 fwd = Vector3.Slerp(path.GetTangent(i0), path.GetTangent(i0 + 1), t); fwd.y = 0f; fwd.Normalize();
                        Vector3 eye = p - fwd * 8f + Vector3.up * 2.6f;
                        var rot = Quaternion.LookRotation(p + Vector3.up * 0.8f + fwd * 20f - eye);
                        string name = $"fp_seq_{k:000}";
                        Shoot(name, eye, rot, "cpu");
                        Shoot(name, eye, rot, "gpuv");
                        log.AppendLine($"{name}: {m:0.0} m, eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0})");
                    }
                }
            }
            finally
            {
                MeshForest.GpuDriven = keepGpu;
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather;
                RaceHandoff.CalendarDay = keepDay;
                SkyModePrefs.Dynamic = keepDyn;
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
            }
            File.WriteAllText(Path.Combine(outDir, "fp_probe.txt"), log.ToString());
            Debug.Log("[ForestGpuProbe] written to " + outDir + "\n" + log);
        }

        /// <summary>+1 if the land falls away to the road's right here, -1 to its left.</summary>
        static float OpenSide(Vector3 p, Vector3 fwd)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float yr = GroundY(p + right * 60f, p.y), yl = GroundY(p - right * 60f, p.y);
            return yr <= yl ? 1f : -1f;
        }

        static float GroundY(Vector3 at, float fallback)
        {
            return Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out var hit, 900f) ? hit.point.y : fallback;
        }
    }
}
