using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SKYLINE'S DEPTH, MEASURED (owner, 2026-10-16: distant buildings and
    /// the rear-view mirror "flicker in and out of constantly overlapping
    /// layers"). Two eyes on the loop (PSX_SKYLINE_ROAD, default I-277)
    /// about PSX_SKYLINE_KM (default 1.5) km either side of uptown, at night,
    /// through the game's own far plane and edge fade. Each view is drawn
    /// twice with the eye moved PSX_DEPTH_SHIFT_M (default 0.03) m sideways:
    /// at a kilometre that moves nothing by a pixel, so a pixel that changes
    /// is two surfaces trading places in the depth buffer. The share of such
    /// pixels in the sky band is the flicker number ([SkyDepth] lines). The
    /// player's car is then parked on the road facing away from uptown and
    /// the cockpit's real mirror drawn twice the same way. PNGs:
    /// Screenshots\skydepth_&lt;view&gt;_{a,b}.png and skydepth_&lt;view&gt;_mirror_{a,b}.png
    /// (the mirror blown up x4). Headless:
    /// -executeMethod PSXRacing.EditorTools.SkylineDepthShots.Run
    /// </summary>
    public static class SkylineDepthShots
    {
        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[SkyDepth] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[SkyDepth] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            if (output != null) output.height = 540;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string road = System.Environment.GetEnvironmentVariable("PSX_SKYLINE_ROAD");
            if (string.IsNullOrEmpty(road)) road = "I-277";
            float km = 1.5f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_SKYLINE_KM") ?? "1.5", System.Globalization.NumberStyles.Float, inv, out km);
            float shift = 0.03f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_DEPTH_SHIFT_M") ?? "0.03", System.Globalization.NumberStyles.Float, inv, out shift);
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            int night = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals("night", System.StringComparison.OrdinalIgnoreCase));
            var go = new GameObject("~skyDepthWorld");
            var world = go.AddComponent<CityWorld>();
            var cv = Object.FindFirstObjectByType<CockpitView>();
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            try
            {
                foreach (int side in new[] { -1, 1 })
                {
                    string name = road.Replace("-", "").ToLowerInvariant() + (side < 0 ? "_south" : "_north");
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
                    if (bei < 0) { Debug.LogError("[SkyDepth] no " + road + " " + name); continue; }
                    var be = map.edges[bei];
                    var at = be.PointAt(bs);
                    float groundY = be.YAt(bs);
                    var eye = new Vector3(at.x, groundY + 2.2f, at.y);
                    var core = new Vector3(map.uptown.x, eye.y + 60f, map.uptown.y);
                    var rot = Quaternion.LookRotation(core - eye);
                    world.EnsureRing(eye, world.ring);
                    TimeOfDay.Apply(night, sun);
                    NightGlow.PreviewAll(true);
                    if (globals != null) globals.Apply();
                    var right = rot * Vector3.right;
                    string fa = "skydepth_" + name + "_a", fb = "skydepth_" + name + "_b";
                    cam.transform.SetPositionAndRotation(eye, rot);
                    world.RefreshSkyline(cam);
                    PSXScreenshotTool.ShotAs(cam, fa, eye, rot);
                    cam.transform.SetPositionAndRotation(eye + right * shift, rot);
                    world.RefreshSkyline(cam);
                    PSXScreenshotTool.ShotAs(cam, fb, eye + right * shift, rot);
                    var flick = Flips(Path.Combine(dir, fa + ".png"), Path.Combine(dir, fb + ".png"), 0f, 0.55f);
                    var sk = world.Skyline;
                    Debug.Log($"[SkyDepth] {name}: {Vector2.Distance(at, map.uptown):0} m from uptown, far {cam.farClipPlane:0}, fog {(globals != null ? globals.fogNear : 0f):0}-{(globals != null ? globals.fogFar : 0f):0}, " +
                              $"skyline {(sk != null ? sk.Shown + "/" + sk.ElementCount : "none")}; SKY BAND FLIPS {flick.flips} of {flick.lit} drawn px ({100f * flick.flips / Mathf.Max(1, flick.lit):0.00}%)");

                    // ---- the skyline ALONE: every other renderer off, so a flip is
                    // tower against tower in the depth buffer and nothing else
                    var skr = sk != null ? sk.GetComponent<MeshRenderer>() : null;
                    if (skr != null)
                    {
                        var off = new List<Renderer>();
                        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            if (r != skr && r.enabled && !r.forceRenderingOff) { r.forceRenderingOff = true; off.Add(r); }
                        string sa = "skydepth_" + name + "_sky_a", sb2 = "skydepth_" + name + "_sky_b";
                        cam.transform.SetPositionAndRotation(eye, rot); world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, sa, eye, rot);
                        cam.transform.SetPositionAndRotation(eye + right * shift, rot); world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, sb2, eye + right * shift, rot);

                        // THE TRUTH: the same towers where they really stand - no
                        // squeeze, a 15 km far plane, no fog - so who covers whom
                        // is the geometry's, and the squeezed frame is scored by
                        // how many of its pixels show a different tower.
                        var skMats = skr.sharedMaterials;
                        var hazeWas = new float[skMats.Length];
                        for (int m = 0; m < skMats.Length; m++) { hazeWas[m] = skMats[m].GetFloat("_Skyline"); skMats[m].SetFloat("_Skyline", 0f); }
                        float farWas = cam.farClipPlane, nearWas = cam.nearClipPlane;
                        float fnWas = Shader.GetGlobalFloat("_PSXFogNear"), ffWas = Shader.GetGlobalFloat("_PSXFogFar");
                        var bWas = skr.bounds;
                        cam.farClipPlane = 15000f; cam.nearClipPlane = 1f;
                        Shader.SetGlobalFloat("_PSXFogNear", 1e6f); Shader.SetGlobalFloat("_PSXFogFar", 2e6f);
                        var mf = sk.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) skr.bounds = mf.sharedMesh.bounds;
                        string st = "skydepth_" + name + "_sky_truth";
                        cam.transform.SetPositionAndRotation(eye, rot);
                        PSXScreenshotTool.ShotAs(cam, st, eye, rot);
                        cam.farClipPlane = farWas; cam.nearClipPlane = nearWas;
                        Shader.SetGlobalFloat("_PSXFogNear", fnWas); Shader.SetGlobalFloat("_PSXFogFar", ffWas);
                        for (int m = 0; m < skMats.Length; m++) skMats[m].SetFloat("_Skyline", hazeWas[m]);
                        skr.bounds = bWas;
                        foreach (var r in off) if (r != null) r.forceRenderingOff = false;
                        var sf = Flips(Path.Combine(dir, sa + ".png"), Path.Combine(dir, sb2 + ".png"), 0f, 1f);
                        var wrong = Flips(Path.Combine(dir, sa + ".png"), Path.Combine(dir, st + ".png"), 0f, 1f);
                        Debug.Log($"[SkyDepth] {name} SKYLINE ALONE: FLIPS {sf.flips} of {sf.lit} drawn px ({100f * sf.flips / Mathf.Max(1, sf.lit):0.00}%); " +
                                  $"UNLIKE THE TRUTH {wrong.flips} of {wrong.lit} px ({100f * wrong.flips / Mathf.Max(1, wrong.lit):0.00}%)");
                    }

                    // ---- the mirror: the car parked here facing away from uptown
                    if (player != null && cv != null)
                    {
                        var away = new Vector3(eye.x - core.x, 0f, eye.z - core.z).normalized;
                        var carRot = Quaternion.LookRotation(away, Vector3.up);
                        var carPos = new Vector3(at.x, groundY, at.y);
                        player.SetActive(true);
                        player.transform.SetPositionAndRotation(carPos, carRot);
                        cv.PreviewShow(true);
                        var ci = player.GetComponentInChildren<CockpitInterior>(true);
                        if (ci == null) { Debug.LogError("[SkyDepth] no cockpit"); continue; }
                        ci.Refit();
                        ci.Show(true, false);
                        ci.Tick(0f, null);
                        var ab = new Texture2D[3];
                        var skyR = new List<Renderer>();
                        if (sk != null) skyR.AddRange(sk.GetComponentsInChildren<Renderer>(true));
                        foreach (var cb in Object.FindObjectsByType<CityBeacons>(FindObjectsSortMode.None)) skyR.AddRange(cb.GetComponentsInChildren<Renderer>(true));
                        for (int k = 0; k < 3; k++)
                        {
                            player.transform.SetPositionAndRotation(carPos + carRot * Vector3.right * ((k == 1 ? 1 : 0) * shift), carRot);
                            foreach (var r in skyR) if (r != null) r.forceRenderingOff = k == 2;
                            ci.PreviewMirror(cam);
                            var rt = typeof(CockpitInterior).GetField("mirrorRT", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ci) as RenderTexture;
                            if (rt == null) { Debug.LogError("[SkyDepth] no mirror texture"); break; }
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            t.Apply();
                            RenderTexture.active = prev;
                            ab[k] = t;
                            var big = new Texture2D(rt.width * 4, rt.height * 4, TextureFormat.RGB24, false);
                            for (int y = 0; y < big.height; y++)
                                for (int x = 0; x < big.width; x++) big.SetPixel(x, y, t.GetPixel(x / 4, y / 4));
                            big.Apply();
                            File.WriteAllBytes(Path.Combine(dir, "skydepth_" + name + "_mirror_" + (k == 0 ? "a" : k == 1 ? "b" : "noskyline") + ".png"), big.EncodeToPNG());
                            Object.DestroyImmediate(big);
                        }
                        if (ab[0] != null && ab[1] != null)
                        {
                            var pa = ab[0].GetPixels32(); var pb = ab[1].GetPixels32();
                            int flips = 0, dark = 0;
                            for (int i = 0; i < pa.Length; i++)
                            {
                                int la = pa[i].r + pa[i].g + pa[i].b, lb = pb[i].r + pb[i].g + pb[i].b;
                                if (Mathf.Abs(la - lb) > 72) flips++;
                                if (la < 30) dark++;
                            }
                            var cam0 = typeof(CockpitInterior).GetField("mirrorCam", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ci) as Camera;
                            Debug.Log($"[SkyDepth] {name} MIRROR: depth bits {(cam0 != null && cam0.targetTexture != null ? cam0.targetTexture.depth : -1)}, far {(cam0 != null ? cam0.farClipPlane : 0f):0}; FLIPS {flips} of {pa.Length} px ({100f * flips / pa.Length:0.00}%), near-black {100f * dark / pa.Length:0.0}%");
                            if (ab[2] != null)
                            {
                                var pc = ab[2].GetPixels32();
                                int sky = 0;
                                for (int i = 0; i < pa.Length; i++)
                                    if (Mathf.Abs((pa[i].r + pa[i].g + pa[i].b) - (pc[i].r + pc[i].g + pc[i].b)) > 24) sky++;
                                Debug.Log($"[SkyDepth] {name} MIRROR: the skyline + beacons draw {100f * sky / pa.Length:0.00}% of the mirror ({skyR.Count} renderers)");
                            }
                        }
                        foreach (var t in ab) if (t != null) Object.DestroyImmediate(t);
                        ci.Show(false, false);
                        cv.PreviewShow(false);
                        player.SetActive(false);
                    }
                    world.DropAll();
                }
            }
            finally
            {
                RaceHandoff.WeatherOverride = oldWeather;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            Debug.Log("[SkyDepth] done");
        }

        /// <summary>Pixels of frame a that differ from frame b by more than a
        /// quantization step, in the band of rows [y0, y1) (shares of the
        /// height, from the top), among the pixels that are not plain sky.</summary>
        static (int flips, int lit) Flips(string a, string b, float y0, float y1)
        {
            if (!File.Exists(a) || !File.Exists(b)) return (0, 0);
            var ta = new Texture2D(2, 2); ta.LoadImage(File.ReadAllBytes(a));
            var tb = new Texture2D(2, 2); tb.LoadImage(File.ReadAllBytes(b));
            if (ta.width != tb.width || ta.height != tb.height) return (0, 0);
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            int w = ta.width, h = ta.height, flips = 0, lit = 0;
            // GetPixels32 rows run bottom-up
            int r0 = Mathf.RoundToInt(h * (1f - y1)), r1 = Mathf.RoundToInt(h * (1f - y0));
            for (int y = r0; y < r1; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int la = pa[i].r + pa[i].g + pa[i].b, lb = pb[i].r + pb[i].g + pb[i].b;
                    // the night sky is ~20-40 a channel: count only drawn things
                    if (Mathf.Max(la, lb) < 150) continue;
                    lit++;
                    if (Mathf.Abs(la - lb) > 72) flips++;
                }
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb);
            return (flips, lit);
        }
    }
}
