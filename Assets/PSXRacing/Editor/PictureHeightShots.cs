using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PICTURE: HD, MEASURED (owner, 2026-10-16: "game resolution increased to
    /// 1080p or 1440p to further progress towards GT4 visual fidelity"). Two
    /// eyes in uptown at night - on I-277 PSX_PIC_KM (default 0.6) km south of
    /// the core looking at the towers, and behind the player's car on the
    /// Tryon street nearest the core - each drawn through the game's own
    /// framebuffer and PSX/Blit at SHARP's 480 lines and at HD's lines for a
    /// PSX_PIC_DISPLAY (default 2000x1125, the owner's window) display, both
    /// shown at that display's size the way the game shows them (point
    /// upscale). PNGs: Screenshots\pich_&lt;spot&gt;_&lt;lines&gt;.png. Then the
    /// camera's own render is TIMED at 120 / 480 / 720 / 1080 / 1125 / 1440
    /// lines (PSX_PIC_FRAMES renders each, default 40, the GPU waited on
    /// before and after): [PicHeight] lines. 120 lines is the floor - culling
    /// and draw submission, which no line count changes - so each line's
    /// excess over it is that height's fill. Headless:
    /// -executeMethod PSXRacing.EditorTools.PictureHeightShots.Run
    /// </summary>
    public static class PictureHeightShots
    {
        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[PicHeight] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[PicHeight] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float km = 0.6f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_PIC_KM") ?? "0.6", System.Globalization.NumberStyles.Float, inv, out km);
            int dispW = 2000, dispH = 1125;
            var ds = (System.Environment.GetEnvironmentVariable("PSX_PIC_DISPLAY") ?? "").Split('x');
            if (ds.Length == 2 && int.TryParse(ds[0], out int dw) && int.TryParse(ds[1], out int dh) && dw > 0 && dh > 0) { dispW = dw; dispH = dh; }
            int frames = 40;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_PIC_FRAMES") ?? "40", out frames);
            frames = Mathf.Max(5, frames);

            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            int night = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals("night", System.StringComparison.OrdinalIgnoreCase));
            var go = new GameObject("~picHeightWorld");
            var world = go.AddComponent<CityWorld>();
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            var keepPixels = PSXQuality.Current;
            try
            {
                var spots = new List<(string name, Vector3 eye, Quaternion rot)>();

                // ---- the loop: I-277 south of the core, looking at the towers
                {
                    float best = float.MaxValue; int bei = -1; float bs = 0f;
                    for (int ei = 0; ei < map.edges.Length; ei++)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.name != "I-277") continue;
                        for (float s = 0f; s <= e.length; s += 10f)
                        {
                            var p = e.PointAt(s);
                            if (p.y > map.uptown.y) continue;
                            float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - km * 1000f);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei >= 0)
                    {
                        var be = map.edges[bei];
                        var at = be.PointAt(bs);
                        var eye = new Vector3(at.x, be.YAt(bs) + 1.6f, at.y);
                        var core = new Vector3(map.uptown.x, eye.y + 45f, map.uptown.y);
                        spots.Add(("loop", eye, Quaternion.LookRotation(core - eye)));
                    }
                    else Debug.LogError("[PicHeight] no I-277");
                }

                // ---- the street: the player's car on the Tryon street nearest
                // the core, the eye where the chase camera stands
                {
                    float best = float.MaxValue; int bei = -1; float bs = 0f;
                    for (int ei = 0; ei < map.edges.Length; ei++)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.name == null || !e.name.Contains("Tryon")) continue;
                        for (float s = 20f; s <= e.length - 20f; s += 5f)
                        {
                            float d = Vector2.Distance(e.PointAt(s), map.uptown);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei >= 0)
                    {
                        var be = map.edges[bei];
                        var at = be.PointAt(bs);
                        var t2 = be.TangentAt(bs).normalized;
                        var fwd = new Vector3(t2.x, 0f, t2.y);
                        var carPos = new Vector3(at.x, be.YAt(bs), at.y);
                        if (player != null)
                        {
                            player.SetActive(true);
                            player.transform.SetPositionAndRotation(carPos, Quaternion.LookRotation(fwd, Vector3.up));
                        }
                        var eye = carPos - fwd * 6.5f + Vector3.up * 2.0f;
                        var look = carPos + fwd * 14f + Vector3.up * 1.2f;
                        spots.Add(("street", eye, Quaternion.LookRotation(look - eye)));
                    }
                    else Debug.LogError("[PicHeight] no Tryon street");
                }

                int hd = PSXQuality.HDLines(dispH);
                foreach (var sp in spots)
                {
                    world.EnsureRing(sp.eye, world.ring);
                    TimeOfDay.Apply(night, sun);
                    NightGlow.PreviewAll(true);
                    if (globals != null) globals.Apply();
                    cam.transform.SetPositionAndRotation(sp.eye, sp.rot);
                    world.RefreshSkyline(cam);

                    // ---- the pictures: SHARP, then HD, at the display's size
                    foreach (var preset in new[] { PSXPixels.Sharp, PSXPixels.HD })
                    {
                        PSXQuality.Current = preset;
                        int lines = preset == PSXPixels.HD ? hd : 480;
                        int w = Even(Mathf.RoundToInt(lines * dispW / (float)dispH));
                        var rt = Render(cam, w, lines);
                        var shown = PSXScreenshotTool.Dithered(rt);
                        var tex = Read(shown);
                        var big = PointTo(tex, dispW, dispH);
                        File.WriteAllBytes(Path.Combine(dir, $"pich_{sp.name}_{lines}.png"), big.EncodeToPNG());
                        if (big != tex) Object.DestroyImmediate(big);
                        Object.DestroyImmediate(tex);
                        if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                        rt.Release(); Object.DestroyImmediate(rt);
                        Debug.Log($"[PicHeight] {sp.name} {PSXQuality.Name}: {w}x{lines} framebuffer shown at {dispW}x{dispH}");
                    }

                    // ---- the cost: the camera's render at each line count
                    var sb = new System.Text.StringBuilder();
                    float floor = 0f;
                    foreach (int lines in new[] { 120, 480, 720, 1080, 1125, 1440 })
                    {
                        int w = Even(Mathf.RoundToInt(lines * 16f / 9f));
                        float ms = TimeRenders(cam, w, lines, frames);
                        if (lines == 120) floor = ms;
                        sb.Append($" {lines}: {ms:0.00} ms (fill {Mathf.Max(0f, ms - floor):0.00})");
                    }
                    Debug.Log($"[PicHeight] {sp.name} TIMING 16:9, {frames} renders each, ms per render:" + sb);
                }
            }
            finally
            {
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                Object.DestroyImmediate(go);
            }
            Debug.Log("[PicHeight] DONE");
        }

        static int Even(int w) => (w & 1) == 1 ? w + 1 : w;

        static RenderTexture Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            Submit(cam, rt);
            return rt;
        }

        static void Submit(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            PSXTexDecode.StampAll();
            RenderPipeline.SubmitRenderRequest(cam, request);
        }

        /// <summary>Wall time per render of the camera into a w x h buffer,
        /// the GPU drained before the clock starts and after it stops (one
        /// pixel read back), so queued work is inside the count.</summary>
        static float TimeRenders(Camera cam, int w, int h, int n)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            for (int i = 0; i < 3; i++) Submit(cam, rt);
            Drain(rt, one);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) Submit(cam, rt);
            Drain(rt, one);
            sw.Stop();
            Object.DestroyImmediate(one);
            rt.Release(); Object.DestroyImmediate(rt);
            return (float)sw.Elapsed.TotalMilliseconds / n;
        }

        static void Drain(RenderTexture rt, Texture2D one)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = prev;
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>The framebuffer at the display's size, nearest-neighbour,
        /// as PSX/Blit shows it.</summary>
        static Texture2D PointTo(Texture2D src, int w, int h)
        {
            if (src.width == w && src.height == h) return src;
            var sp = src.GetPixels32();
            var dp = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = Mathf.Min(src.height - 1, y * src.height / h);
                for (int x = 0; x < w; x++)
                    dp[y * w + x] = sp[sy * src.width + Mathf.Min(src.width - 1, x * src.width / w)];
            }
            var big = new Texture2D(w, h, TextureFormat.RGB24, false);
            big.SetPixels32(dp);
            big.Apply();
            return big;
        }
    }
}
