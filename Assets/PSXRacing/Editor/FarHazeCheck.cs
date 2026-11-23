using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FAR HAZE CHECK (owner 2026-11-23, PC60, Bat Cave in the rain: a far
    /// mountain "glowing white like a white dwarf star" against a dark grey
    /// sky). For every venue, spot, weather and hour: the frame as the PC
    /// player draws it, and the brightness of the FAR land just under the
    /// skyline against the sky just above it, column by column.
    ///
    /// Two masks per pose (they do not change with the hour): SKY (the camera
    /// clears to pure green with no skybox: every pixel no geometry covers) and
    /// FAR (the fog magenta from PSX_FH_FAR metres, the far haze off: every
    /// pixel of something that far). A column counts when its skyline - the
    /// first land under at least 8 rows of sky - has far land in the 4 rows
    /// under it and sky in the 4 over it (2 rows of gap each side). Its land
    /// and sky luminances (sRGB decoded, Rec. 709) go into 16-column bins; the
    /// row is the whole frame's ratio and the worst bin's (bins of 6+ columns).
    ///
    /// Env: PSX_FH_VENUES (all stages + the three Charlotte circuits), PSX_FH_SPOTS
    /// ("LakeLure:7970c,3000,10500;..." metres, c = the chase view, else the
    /// vista 25 m up toward the open side; default three spots at 20/50/80%),
    /// PSX_FH_WEATHERS (clear,overcast,rain,fog,snow), PSX_FH_HOURS (noon,dusk,
    /// night), PSX_FH_DYN (1 = the dynamic sky, 0 = the photographs), PSX_FH_TAG,
    /// PSX_FH_KEEP (venues whose frames are kept; the rest are measured and
    /// deleted). Writes Screenshots\farhaze\&lt;tag&gt;\farhaze_check.txt: one row a
    /// frame, and FAIL where a limited row (overcast, rain, fog, snow at any
    /// hour; clear at dusk and night) passes <see cref="Limit"/>.
    /// </summary>
    public static class FarHazeCheck
    {
        /// <summary>The worst bin's far land may be this much brighter than its sky.</summary>
        public const float Limit = 1.15f;
        /// <summary>A clear day's limit (aerial haze is lighter than its sky): reported, looser.</summary>
        public const float LimitClearDay = 1.35f;
        public const string ReportName = "farhaze_check.txt";

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        /// <summary>Several passes in one editor (quick looks only; the
        /// before/after table is two fresh processes): PSX_FH_PASSES =
        /// "off:1,on:1,off:0,on:0" (PSX_FARHAZE2 : PSX_FH_DYN), each tagged
        /// &lt;switch&gt;_&lt;dyn|photo&gt;.</summary>
        public static void RunPasses()
        {
            foreach (var pass in Env("PSX_FH_PASSES", "off:1,on:1").Split(',', '+'))
            {
                var kv = pass.Split(':');
                System.Environment.SetEnvironmentVariable("PSX_FARHAZE2", kv[0] == "off" ? "0" : "1");
                System.Environment.SetEnvironmentVariable("PSX_FH_DYN", kv[1]);
                System.Environment.SetEnvironmentVariable("PSX_FH_TAG", kv[0] + "_" + (kv[1] == "1" ? "dyn" : "photo"));
                Run();
            }
        }

        public static void Run()
        {
            string tag = Env("PSX_FH_TAG", Env("PSX_FARHAZE2", "1") != "0" ? "on" : "off");
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            string outDir = Path.Combine(root, "farhaze", tag);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            var rep = new System.Text.StringBuilder();
            rep.AppendLine($"# far haze check, tag {tag}, PSX_FARHAZE2 {(Env("PSX_FARHAZE2", "1") != "0" ? "on" : "off")}, dynamic sky {Env("PSX_FH_DYN", "1")}, limit {Limit} (clear day {LimitClearDay})");
            rep.AppendLine("# venue spot view weather hour | cols ratio worst | skyL landL | verdict");
            int fails = 0, rows = 0;

            var venues = new List<string>();
            string vArg = Env("PSX_FH_VENUES", "").Replace('+', ',');
            if (vArg.Length > 0) venues.AddRange(vArg.Split(','));
            else
            {
                for (int i = 0; i < TrackCatalog.Count; i++)
                    if (TrackCatalog.At(i).stage) venues.Add(TrackCatalog.At(i).id);
                venues.AddRange(new[] { "UptownLoop", "IndependenceSprint", "TryonSprint" });
            }
            var spotMap = new Dictionary<string, string>();
            foreach (var s in Env("PSX_FH_SPOTS", "LakeLure:7970c,3000,10500").Split(';'))
            {
                var kv = s.Split(':');
                if (kv.Length == 2) spotMap[kv[0].Trim()] = kv[1];
            }
            var keep = new HashSet<string>(Env("PSX_FH_KEEP", "LakeLure").Split(',', '+'));
            string[] weathers = Env("PSX_FH_WEATHERS", "clear,overcast,rain,fog,snow").Split(',', '+');
            string[] hours = Env("PSX_FH_HOURS", "noon,dusk,night").Split(',', '+');
            bool dyn = Env("PSX_FH_DYN", "1") == "1";
            float farM = float.Parse(Env("PSX_FH_FAR", "0"), CultureInfo.InvariantCulture);

            bool keepDyn = SkyModePrefs.Dynamic;
            int keepWeather = RaceHandoff.WeatherOverride;
            try
            {
                SkyModePrefs.Dynamic = dyn;
                RaceHandoff.WeatherOverride = -1;
                foreach (string venue in venues)
                {
                    string id = venue.Trim();
                    TrackCatalog.TrackDef def = null;
                    for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
                    if (def == null || !File.Exists("Assets/PSXRacing/Scenes/" + id + ".unity")) { rep.AppendLine($"{id}: no scene - skipped"); continue; }
                    if (!PSXScreenshotTool.Open(def, out var cam, out _)) { rep.AppendLine($"{id}: did not open - skipped"); continue; }
                    var path = Object.FindFirstObjectByType<TrackPath>();
                    var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                    var globals = Object.FindFirstObjectByType<PSXGlobals>();
                    var forest = Object.FindFirstObjectByType<MeshForest>();
                    var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                    var dressing = Object.FindFirstObjectByType<SeasonDress>();
                    if (path == null || globals == null || sun == null) { rep.AppendLine($"{id}: scene parts missing - skipped"); continue; }
                    if (output != null) output.height = 480;
                    var sc = cam.GetComponent<StageCulling>();
                    if (sc != null) sc.Configure(cam);
                    if (dressing != null)
                    {
                        typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                                      System.Reflection.BindingFlags.Instance)?.Invoke(dressing, null);
                        dressing.Apply((int)Season.Fall);
                    }
                    bool city = !def.stage;
                    float far = farM > 0f ? farM : city ? 380f : 1500f;
                    float len = path.Count * TrackCatalog.Spacing;
                    string spots = spotMap.TryGetValue(id, out var sp) ? sp
                        : string.Join(",", new[] { 0.2f, 0.5f, 0.8f }.Select(f => Mathf.RoundToInt(len * f).ToString(CultureInfo.InvariantCulture)));
                    rep.AppendLine($"{id}: far plane {cam.farClipPlane:0} m, detail {TimeOfDay.FarEdgeM:0} m, horizon {TimeOfDay.HorizonM:0} m, far mask from {far:0} m, spots {spots}");
                    foreach (string spotRaw in spots.Split(',', '+'))
                    {
                        string spot = spotRaw.Trim();
                        bool chase = spot.EndsWith("c");
                        float m = float.Parse(chase ? spot.Substring(0, spot.Length - 1) : spot, CultureInfo.InvariantCulture);
                        int at = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 2, path.Count - 12);
                        Vector3 p = path.GetPoint(at);
                        Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
                        Vector3 eye; Quaternion rot;
                        if (chase)
                        {
                            eye = p - fwd * 8f + Vector3.up * 2.6f;
                            rot = Quaternion.LookRotation(p + Vector3.up * 0.8f + fwd * 20f - eye);
                        }
                        else
                        {
                            eye = p + Vector3.up * 25f;
                            rot = Quaternion.LookRotation(Quaternion.Euler(0f, OpenSide(path, at) * 60f, 0f) * fwd);
                        }
                        string view = chase ? "chase" : "vista";
                        string stem = $"{id}_{Mathf.RoundToInt(m)}_{view}";

                        // THE MASKS: clear noon, once per pose
                        Force("clear");
                        TimeOfDay.Apply(Hour("noon"), sun);
                        var keepFlags = cam.clearFlags; var keepBg = cam.backgroundColor;
                        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 1f, 0f, 1f);
                        var keepSky = RenderSettings.skybox; RenderSettings.skybox = null;
                        globals.Apply();
                        Color32[] skyM = Shoot(cam, forest, eye, rot, outDir, stem + "_mask_sky", out int w, out int h);
                        cam.clearFlags = keepFlags; cam.backgroundColor = keepBg; RenderSettings.skybox = keepSky;
                        TimeOfDay.Apply(Hour("noon"), sun);
                        globals.fogColor = new Color(1f, 0f, 1f, 1f); globals.fogNear = far; globals.fogFar = far + 1f; globals.fogCurve = 1f;
                        globals.Apply();
                        Shader.SetGlobalVector("_PSXFarHaze", Vector4.zero);
                        Shader.SetGlobalVector("_PSXAerial", Vector4.zero);
                        Color32[] farMk = Shoot(cam, forest, eye, rot, outDir, stem + "_mask_far", out _, out _);
                        if (skyM == null || farMk == null) { rep.AppendLine($"{stem}: no frame"); continue; }

                        foreach (string wx in weathers)
                            foreach (string hr in hours)
                            {
                                int hour = Hour(hr.Trim());
                                if (hour < 0) continue;
                                Force(wx.Trim());
                                TimeOfDay.Apply(hour, sun);
                                globals.Apply();
                                if (dyn) DynamicSky.PrimeClouds(eye);
                                string name = $"{stem}_{wx.Trim()}_{hr.Trim()}";
                                Color32[] px = Shoot(cam, forest, eye, rot, outDir, name, out _, out _);
                                if (px == null) { rep.AppendLine(name + ": no frame"); continue; }
                                Measure(px, skyM, farMk, w, h, out int cols, out float ratio, out float worst, out float sl, out float ll);
                                bool clearDay = wx.Trim() == "clear" && hr.Trim() == "noon";
                                float lim = clearDay ? LimitClearDay : Limit;
                                string verdict = cols < 6 ? "n/a (no far skyline)" : worst > lim ? "FAIL" : "ok";
                                if (verdict == "FAIL") fails++;
                                rows++;
                                rep.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} | {5} {6:0.00} {7:0.00} | {8:0.000} {9:0.000} | {10}",
                                    id, Mathf.RoundToInt(m), view, wx.Trim(), hr.Trim(), cols, ratio, worst, sl, ll, verdict));
                                if (!keep.Contains(id) && !(wx.Trim() == "clear" && hr.Trim() == "noon"))
                                    File.Delete(Path.Combine(outDir, name + ".png"));
                            }
                        if (!keep.Contains(id))
                        {
                            File.Delete(Path.Combine(outDir, stem + "_mask_sky.png"));
                            File.Delete(Path.Combine(outDir, stem + "_mask_far.png"));
                        }
                    }
                }
            }
            finally
            {
                Seasons.Forced = null; Seasons.Invalidate();
                RaceHandoff.WeatherOverride = keepWeather;
                SkyModePrefs.Dynamic = keepDyn;
                TimeOfDay.FarEdgeM = 0f; TimeOfDay.HorizonM = 0f;
            }
            rep.AppendLine($"# FARHAZE {(fails == 0 ? "PASS" : "FAIL")}: {fails} of {rows} frames over the limit");
            File.WriteAllText(Path.Combine(outDir, ReportName), rep.ToString());
            Debug.Log("[FarHaze] " + rows + " frames, " + fails + " over the limit; " + outDir);
        }

        static int Hour(string name)
        {
            for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == name.ToLower()) return h;
            return -1;
        }

        /// <summary>The sky and the air for one named weather (overcast = the
        /// closed sky with nothing falling, which lights as Rain).</summary>
        static void Force(string word)
        {
            Seasons.Forced = null; Seasons.Invalidate();
            var w = Seasons.Now;
            w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f; w.sky = SkyKind.Clear;
            switch (word)
            {
                case "overcast": w.sky = SkyKind.Overcast; break;
                case "rain": w.sky = SkyKind.Overcast; w.precip = PrecipKind.Rain; w.precipRate = 0.6f; break;
                case "fog": w.sky = SkyKind.Overcast; w.fog = true; break;
                case "snow": w.sky = SkyKind.Overcast; w.precip = PrecipKind.Snow; w.precipRate = 0.5f; break;
            }
            Seasons.Forced = w;
        }

        static Color32[] Shoot(Camera cam, MeshForest forest, Vector3 eye, Quaternion rot, string dir, string name, out int w, out int h)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            SunShadows.RenderFor(cam);
            if (forest != null) forest.DrawNow(cam);
            string rel = "farhaze/" + Path.GetFileName(dir) + "/" + name;
            PSXScreenshotTool.ShotAs(cam, rel, eye, rot);
            string file = Path.Combine(dir, name + ".png");
            w = h = 0;
            if (!File.Exists(file)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(file));
            w = tex.width; h = tex.height;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static float Lin(byte c) { float v = c / 255f; return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
        static float Lum(Color32 c) => 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        static bool IsSky(Color32 c) => c.g > c.r + 100 && c.g > c.b + 100;
        static bool IsFar(Color32 c) => c.r > c.g + 70 && c.b > c.g + 70;

        /// <summary>Column by column, top down (texture rows run bottom up).</summary>
        internal static void Measure(Color32[] px, Color32[] sky, Color32[] far, int w, int h,
                                     out int cols, out float ratio, out float worst, out float skyL, out float landL)
        {
            const int Bin = 16, Gap = 2, Band = 4, MinSky = 8;
            int bins = (w + Bin - 1) / Bin;
            var bs = new double[bins]; var bl = new double[bins]; var bn = new int[bins];
            double ts = 0, tl = 0; cols = 0;
            for (int x = 0; x < w; x++)
            {
                int run = 0, yS = -1;
                for (int r = 0; r < h; r++)          // r = rows from the top
                {
                    var c = sky[(h - 1 - r) * w + x];
                    if (IsSky(c)) { run++; continue; }
                    if (run >= MinSky) { yS = r; }
                    break;
                }
                if (yS < 0 || yS + Gap + Band >= h || yS - Gap - Band < 0) continue;
                bool ok = true; double s = 0, l = 0;
                for (int k = 0; k < Band && ok; k++)
                {
                    int rl = yS + Gap + k, rs = yS - Gap - 1 - k;
                    int il = (h - 1 - rl) * w + x, isk = (h - 1 - rs) * w + x;
                    if (IsSky(sky[il]) || !IsFar(far[il]) || !IsSky(sky[isk])) ok = false;
                    l += Lum(px[il]); s += Lum(px[isk]);
                }
                if (!ok) continue;
                int b = x / Bin; bs[b] += s; bl[b] += l; bn[b]++;
                ts += s; tl += l; cols++;
            }
            ratio = ts > 1e-6 ? (float)(tl / ts) : 0f;
            worst = 0f;
            for (int b = 0; b < bins; b++)
                if (bn[b] >= 6 && bs[b] > 1e-6) worst = Mathf.Max(worst, (float)(bl[b] / bs[b]));
            skyL = cols > 0 ? (float)(ts / (cols * Band)) : 0f;
            landL = cols > 0 ? (float)(tl / (cols * Band)) : 0f;
        }

        static float OpenSide(TrackPath path, int at)
        {
            Vector3 p = path.GetPoint(at);
            Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
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
