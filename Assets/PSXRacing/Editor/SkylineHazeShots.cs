using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT FADES THE FAR TOWERS (owner, 2026-10-16, a night shot on the loop:
    /// "The buildings look hazy and faded"). One eye either side of uptown on
    /// PSX_SKYLINE_ROAD (default I-277), PSX_SKYLINE_KM (default 1.0) from it,
    /// at PSX_HAZE_HOUR (default night), the whole world drawn four ways: as
    /// the game does, without the far skyline's own haze (_Skyline), without
    /// the streamed world's edge fade (the _PSXFog band, curve pushed to 64),
    /// and without both. [SkyHaze] lines give the towers' mean brightness,
    /// saturation and contrast (pixels in the sky rows that are not sky);
    /// Screenshots\skyhaze_&lt;view&gt;.png is the four side by side. Headless:
    /// -executeMethod PSXRacing.EditorTools.SkylineHazeShots.Run
    /// </summary>
    public static class SkylineHazeShots
    {
        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { Debug.LogError("[SkyHaze] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[SkyHaze] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            // PSX_HAZE_LINES: the framebuffer height (the game at MAX draws the screen's own)
            int lines = 540;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_HAZE_LINES") ?? "540", out lines);
            if (output != null) output.height = lines;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string road = System.Environment.GetEnvironmentVariable("PSX_SKYLINE_ROAD");
            if (string.IsNullOrEmpty(road)) road = "I-277";
            float km = 1.0f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_SKYLINE_KM") ?? "1.0", System.Globalization.NumberStyles.Float, inv, out km);
            string hourName = System.Environment.GetEnvironmentVariable("PSX_HAZE_HOUR");
            if (string.IsNullOrEmpty(hourName)) hourName = "night";
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName, System.StringComparison.OrdinalIgnoreCase));
            if (hour < 0) { Debug.LogError("[SkyHaze] no hour " + hourName); return; }
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var go = new GameObject("~skyHazeWorld");
            var world = go.AddComponent<CityWorld>();
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            // PSX_HAZE_TEX=both: the game drawn twice instead, CLASSIC then ENHANCED textures
            bool texPair = System.Environment.GetEnvironmentVariable("PSX_HAZE_TEX") == "both";
            string[] variants = texPair ? new[] { "classic", "enhanced" } : new[] { "game", "no_skyline_haze", "no_edge_fade", "neither" };
            bool texWas = TexturePrefs.Enhanced;
            try
            {
                foreach (int side in new[] { -1, 1 })
                {
                    string name = road.Replace("-", "").ToLowerInvariant() + (side < 0 ? "_south" : "_north") + "_" + hourName.ToLowerInvariant() + "_" + lines;
                    float best = float.MaxValue; int bei = -1; float bs = 0f;
                    for (int ei = 0; ei < map.edges.Length; ei++)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.name != road) continue;
                        for (float s = 0f; s <= e.length; s += 10f)
                        {
                            var p = e.PointAt(s);
                            if (Mathf.Sign(p.y - map.uptown.y) != side) continue;
                            float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - km * 1000f);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei < 0) { Debug.LogError("[SkyHaze] no " + road + " " + name); continue; }
                    var be = map.edges[bei];
                    var at = be.PointAt(bs);
                    var eye = new Vector3(at.x, be.YAt(bs) + 1.2f, at.y);
                    var rot = Quaternion.LookRotation(new Vector3(map.uptown.x, eye.y + 60f, map.uptown.y) - eye);
                    world.EnsureRing(eye, world.ring);
                    TimeOfDay.Apply(hour, sun);
                    NightGlow.PreviewAll(true);
                    if (globals != null) globals.Apply();
                    cam.transform.SetPositionAndRotation(eye, rot);
                    world.RefreshSkyline(cam);
                    var sk = world.Skyline;
                    var skMats = sk != null ? sk.GetComponent<MeshRenderer>().sharedMaterials : new Material[0];
                    var hazeWas = new float[skMats.Length];
                    for (int m = 0; m < skMats.Length; m++) hazeWas[m] = skMats[m].GetFloat("_Skyline");
                    float curveWas = globals != null ? globals.fogCurve : 0f;
                    var shots = new Texture2D[variants.Length];
                    for (int v = 0; v < variants.Length; v++)
                    {
                        bool noHaze = !texPair && (v == 1 || v == 3), noFade = !texPair && (v == 2 || v == 3);
                        if (texPair) { TexturePrefs.Enhanced = v == 1; TextureTier.ApplyAll(); }
                        for (int m = 0; m < skMats.Length; m++) skMats[m].SetFloat("_Skyline", noHaze ? 1e9f : hazeWas[m]);
                        if (globals != null) { globals.fogCurve = noFade ? 64f : curveWas; globals.Apply(); }
                        string f = "skyhaze_" + name + "_" + variants[v];
                        PSXScreenshotTool.ShotAs(cam, f, eye, rot);
                        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(dir, f + ".png")));
                        shots[v] = t;
                    }
                    for (int m = 0; m < skMats.Length; m++) skMats[m].SetFloat("_Skyline", hazeWas[m]);
                    if (globals != null) { globals.fogCurve = curveWas; globals.Apply(); }
                    Debug.Log($"[SkyHaze] {name}: {Vector2.Distance(at, map.uptown):0} m from uptown, fog {(globals != null ? globals.fogNear : 0f):0}-{(globals != null ? globals.fogFar : 0f):0}, skyline haze {(hazeWas.Length > 0 ? hazeWas[0] : 0f):0} m");
                    for (int v = 0; v < variants.Length; v++)
                    {
                        var s = Score(shots[v], shots[variants.Length - 1]);
                        Debug.Log($"[SkyHaze] {name} {variants[v]}: towers {s.n} px, mean lum {s.lum:0.0}, sat {s.sat:0.000}, contrast {s.std:0.0}; moved vs {variants[variants.Length - 1]} {s.moved:0.0}% of them");
                    }
                    int w = shots[0].width, h = shots[0].height;
                    var sheet = new Texture2D(w * 2 + 4, h * 2 + 4, TextureFormat.RGB24, false);
                    sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
                    for (int v = 0; v < variants.Length; v++)
                        sheet.SetPixels32((v % 2) * (w + 4), (1 - v / 2) * (h + 4), w, h, shots[v].GetPixels32());
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, "skyhaze_" + name + ".png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    foreach (var t in shots) Object.DestroyImmediate(t);
                    world.DropAll();
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                if (texPair) { TexturePrefs.Enhanced = texWas; TextureTier.ApplyAll(); }
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            Debug.Log("[SkyHaze] done");
        }

        /// <summary>The towers: pixels in the top 60% of the frame that the
        /// no-fade-no-haze frame draws brighter than the night sky (sum over 200),
        /// scored in frame a.</summary>
        static (int n, float lum, float sat, float std, float moved) Score(Texture2D a, Texture2D clean)
        {
            var pa = a.GetPixels32(); var pc = clean.GetPixels32();
            int w = a.width, h = a.height, n = 0, moved = 0;
            double sl = 0, sl2 = 0, ss = 0;
            for (int y = Mathf.RoundToInt(h * 0.4f); y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var c = pc[i];
                    if (c.r + c.g + c.b < 200) continue;
                    var p = pa[i];
                    float l = (p.r + p.g + p.b) / 3f;
                    int mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b)), mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                    n++; sl += l; sl2 += l * l; ss += mx > 0 ? (mx - mn) / (float)mx : 0f;
                    if (Mathf.Abs((p.r + p.g + p.b) - (c.r + c.g + c.b)) > 24) moved++;
                }
            if (n == 0) return (0, 0f, 0f, 0f, 0f);
            double mean = sl / n;
            return (n, (float)mean, (float)(ss / n), (float)System.Math.Sqrt(System.Math.Max(0.0, sl2 / n - mean * mean)), 100f * moved / n);
        }
    }
}
