using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CITY FROM A MILE OUT (owner, 2026-10-24, an NFS 2015 night frame:
    /// "The city buildings look black and lit up from distance. Distant
    /// building lights are visible from a mile away"). Eyes on the freeways at
    /// chase height looking at uptown - I-77 1.6 km south, I-277 1.0 km south,
    /// I-77 3 km north - through the game's own draw distance, two-tile ring,
    /// skyline, far lights, grade and halation, at HD lines for the owner's
    /// 2000x1125 window. PNGs: Screenshots\far_&lt;spot&gt;_&lt;hour&gt;[_tag].png.
    ///   PSX_FAR_HOURS=night,dusk,noon (default night,dusk)
    ///   PSX_FAR_SPOTS=i77s16,i277s10,i77n30 (default all)
    ///   PSX_FAR_TAG=before (a suffix)
    /// Headless: -executeMethod PSXRacing.EditorTools.CityFarShots.Run
    /// </summary>
    public static class CityFarShots
    {
        static readonly (string name, string road, int side, float km, float up)[] Spots =
        {
            ("i77s16", "I-77", -1, 1.6f, 2.2f),
            ("i277s10", "I-277", -1, 1.0f, 2.2f),
            ("i77n30", "I-77", 1, 3.0f, 2.2f),
            // the city from above the trees, as the owner's NFS frame sees it
            // (a parking deck's roof is about this high)
            ("high45", "I-77", 1, 3.0f, 45f),
        };

        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[FarShots] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[FarShots] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            if (player != null) player.SetActive(false);
            string tag = System.Environment.GetEnvironmentVariable("PSX_FAR_TAG");
            tag = string.IsNullOrEmpty(tag) ? "" : "_" + tag;
            var only = new HashSet<string>((System.Environment.GetEnvironmentVariable("PSX_FAR_SPOTS") ?? "").Split(','));
            only.Remove("");
            var hours = new List<int>();
            foreach (var hn in (System.Environment.GetEnvironmentVariable("PSX_FAR_HOURS") ?? "night,dusk").Split(','))
            {
                int hi = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hn.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (hi >= 0) hours.Add(hi);
            }
            const int dispW = 2000, dispH = 1125;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var keepPixels = PSXQuality.Current;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            var go = new GameObject("~farShotsWorld");
            var world = go.AddComponent<CityWorld>();
            var log = new System.Text.StringBuilder();
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                int lines = PSXQuality.HDLines(dispH);
                int w = Even(Mathf.RoundToInt(lines * dispW / (float)dispH));
                bool timeIt = System.Environment.GetEnvironmentVariable("PSX_FAR_TIME") == "1";
                foreach (var sp in Spots)
                {
                    if (only.Count > 0 && !only.Contains(sp.name)) continue;
                    float best = float.MaxValue; int bei = -1; float bs = 0f;
                    for (int ei = 0; ei < map.edges.Length; ei++)
                    {
                        var e = map.edges[ei];
                        if (e.link || e.name != sp.road) continue;
                        for (float s = 0f; s <= e.length; s += 10f)
                        {
                            var p = e.PointAt(s);
                            if (Mathf.Sign(p.y - map.uptown.y) != sp.side) continue;
                            float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - sp.km * 1000f);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei < 0) { Debug.LogError($"[FarShots] no {sp.road} for {sp.name}"); continue; }
                    var be = map.edges[bei];
                    var at = be.PointAt(bs);
                    var eye = new Vector3(at.x, be.YAt(bs) + sp.up, at.y);
                    var core = new Vector3(map.uptown.x, eye.y + 40f, map.uptown.y);
                    var rot = Quaternion.LookRotation(core - eye);
                    world.EnsureRing(eye, world.ring);
                    foreach (int hour in hours)
                    {
                        TimeOfDay.Apply(hour, sun);
                        NightGlow.PreviewAll(hour >= TimeOfDay.Dusk);
                        if (globals != null) globals.Apply();
                        cam.transform.SetPositionAndRotation(eye, rot);
                        world.RefreshSkyline(cam);
                        var rt = new RenderTexture(w, lines, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                        rt.Create();
                        StreetLights.Push(cam.transform.position, cam.transform.forward);
                        PSXTexDecode.StampAll();
                        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                        var shown = PSXScreenshotTool.Dithered(rt);
                        var tex = Read(shown);
                        var big = PointTo(tex, dispW, dispH);
                        string file = $"far_{sp.name}_{TimeOfDay.All[hour].name.ToLowerInvariant()}{tag}.png";
                        File.WriteAllBytes(Path.Combine(dir, file), big.EncodeToPNG());
                        if (big != tex) Object.DestroyImmediate(big);
                        Object.DestroyImmediate(tex);
                        if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                        rt.Release(); Object.DestroyImmediate(rt);
                        // THE COST: the camera's render with the far city and without it
                        if (timeIt && hour == hours[0] && world.Far != null)
                        {
                            float withMs = TimeRenders(cam, w, lines, 30);
                            world.Far.gameObject.SetActive(false);
                            float withoutMs = TimeRenders(cam, w, lines, 30);
                            world.Far.gameObject.SetActive(true);
                            world.RefreshSkyline(cam);
                            log.Append($"{sp.name} TIME {w}x{lines}: {withMs:0.00} ms with the far city, {withoutMs:0.00} ms without (+{withMs - withoutMs:0.00})\n");
                        }
                    }
                    var sk = world.Skyline;
                    log.Append($"{sp.name}: eye ({eye.x:0},{eye.y:0.0},{eye.z:0}) {Vector2.Distance(at, map.uptown):0} m from uptown, far {cam.farClipPlane:0} m, {w}x{lines}, tiles {world.LiveTiles}, skyline {(sk != null ? sk.Shown + "/" + sk.ElementCount : "none")}" +
                               $", fog {(globals != null ? globals.fogNear.ToString("0") + "-" + globals.fogFar.ToString("0") : "?")}, far {(world.Far != null ? world.Far.Summary : "none")}\n");
                    world.DropAll();
                }
            }
            finally
            {
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "far_shots.txt"), log.ToString());
            Debug.Log("[FarShots] DONE | " + log.ToString().Replace("\n", " | "));
        }

        static int Even(int w) => (w & 1) == 1 ? w + 1 : w;

        /// <summary>Wall time per render, the GPU drained either side
        /// (PictureHeightShots' method).</summary>
        static float TimeRenders(Camera cam, int w, int h, int n)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            void Submit()
            {
                StreetLights.Push(cam.transform.position, cam.transform.forward);
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
            }
            void Drain()
            {
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                one.Apply();
                RenderTexture.active = prev;
            }
            for (int i = 0; i < 3; i++) Submit();
            Drain();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) Submit();
            Drain();
            sw.Stop();
            Object.DestroyImmediate(one);
            rt.Release(); Object.DestroyImmediate(rt);
            return (float)sw.Elapsed.TotalMilliseconds / n;
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
