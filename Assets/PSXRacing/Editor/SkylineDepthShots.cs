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
    /// the cockpit's real mirror drawn twice the same way, and its FACE seen
    /// from the driver's eye as the car moves in sub-millimetre steps (MIRROR
    /// FACE lines: the face against the housing behind it). PNGs:
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
            // PSX_SKYLINE_LANDMARK (a landmark byte, e.g. 1 the spired crown):
            // the eyes stand PSX_SKYLINE_KM from THAT tower and look at its
            // crown, and the crown's own screen box is scored too (CROWN lines)
            int mark = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_SKYLINE_LANDMARK") ?? "0", out mark);
            Vector2 aim = map.uptown;
            var crownParts = new List<CityMap.Footprint>();
            if (mark > 0)
            {
                int outline = System.Array.FindIndex(map.footprints, f => f.landmark == mark && !f.part);
                if (outline < 0) { Debug.LogError("[SkyDepth] no landmark " + mark); return; }
                aim = map.footprints[outline].centre;
                foreach (var f in map.footprints) if (f.outline == outline || f == map.footprints[outline]) crownParts.Add(f);
            }
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
                            if (Mathf.Sign(p.y - aim.y) != side) continue;
                            float d = Mathf.Abs(Vector2.Distance(p, aim) - km * 1000f);
                            if (d < best) { best = d; bei = ei; bs = s; }
                        }
                    }
                    if (bei < 0) { Debug.LogError("[SkyDepth] no " + road + " " + name); continue; }
                    var be = map.edges[bei];
                    var at = be.PointAt(bs);
                    float groundY = be.YAt(bs);
                    var eye = new Vector3(at.x, groundY + 2.2f, at.y);
                    var core = mark > 0 ? new Vector3(aim.x, eye.y + 200f, aim.y) : new Vector3(map.uptown.x, eye.y + 60f, map.uptown.y);
                    if (mark > 0) name += "_lm" + mark;
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
                        // the skyline's three draws (CitySkyline.Draws): every
                        // renderer on its mesh, not the beacons under it
                        var skMesh = sk.GetComponent<MeshFilter>().sharedMesh;
                        var skRs = new List<MeshRenderer>();
                        foreach (var r in sk.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var rf = r.GetComponent<MeshFilter>();
                            if (rf != null && rf.sharedMesh == skMesh) skRs.Add(r);
                        }
                        var off = new List<Renderer>();
                        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            if (!(r is MeshRenderer mr0 && skRs.Contains(mr0)) && r.enabled && !r.forceRenderingOff) { r.forceRenderingOff = true; off.Add(r); }
                        string sa = "skydepth_" + name + "_sky_a", sb2 = "skydepth_" + name + "_sky_b";
                        cam.transform.SetPositionAndRotation(eye, rot); world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, sa, eye, rot);
                        cam.transform.SetPositionAndRotation(eye + right * shift, rot); world.RefreshSkyline(cam);
                        PSXScreenshotTool.ShotAs(cam, sb2, eye + right * shift, rot);

                        // THE TRUTH: the same towers where they really stand - no
                        // squeeze, a 15 km far plane, no fog - so who covers whom
                        // is the geometry's, and the squeezed frame is scored by
                        // how many of its pixels show a different tower.
                        var skMats = new List<Material>();
                        foreach (var r in skRs) skMats.AddRange(r.sharedMaterials);
                        var hazeWas = new float[skMats.Count];
                        for (int m = 0; m < skMats.Count; m++) { hazeWas[m] = skMats[m].GetFloat("_Skyline"); skMats[m].SetFloat("_Skyline", 0f); }
                        float farWas = cam.farClipPlane, nearWas = cam.nearClipPlane;
                        float fnWas = Shader.GetGlobalFloat("_PSXFogNear"), ffWas = Shader.GetGlobalFloat("_PSXFogFar");
                        var bWas = skr.bounds;
                        cam.farClipPlane = 15000f; cam.nearClipPlane = 1f;
                        Shader.SetGlobalFloat("_PSXFogNear", 1e6f); Shader.SetGlobalFloat("_PSXFogFar", 2e6f);
                        var mf = sk.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) foreach (var r in skRs) r.bounds = mf.sharedMesh.bounds;
                        string st = "skydepth_" + name + "_sky_truth";
                        cam.transform.SetPositionAndRotation(eye, rot);
                        PSXScreenshotTool.ShotAs(cam, st, eye, rot);
                        cam.farClipPlane = farWas; cam.nearClipPlane = nearWas;
                        Shader.SetGlobalFloat("_PSXFogNear", fnWas); Shader.SetGlobalFloat("_PSXFogFar", ffWas);
                        for (int m = 0; m < skMats.Count; m++) skMats[m].SetFloat("_Skyline", hazeWas[m]);
                        foreach (var r in skRs) r.bounds = bWas;

                        // ---- THE CROWN (PSX_SKYLINE_LANDMARK): the pair above scored in
                        // the tower's own screen box, and a SWEEP - four frames, the eye
                        // moved a quarter of a pixel (at the tower) each time. An edge
                        // that moves turns a pixel over once; a pixel that turns on and
                        // off again is a member thinner than a pixel popping.
                        if (mark > 0)
                        {
                            float dist = Vector2.Distance(at, aim);
                            float onePx = 2f * dist * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) / 540f;
                            cam.transform.SetPositionAndRotation(eye, rot);
                            var vb = CrownBox(cam, crownParts, groundY);
                            var shots = new List<string> { sa, sb2 };
                            for (int k = 0; k < 4; k++)
                            {
                                string sw = "skydepth_" + name + "_sweep" + k;
                                var e2 = eye + right * (0.25f * onePx * k);
                                cam.transform.SetPositionAndRotation(e2, rot); world.RefreshSkyline(cam);
                                PSXScreenshotTool.ShotAs(cam, sw, e2, rot);
                                shots.Add(sw);
                            }
                            CrownScore(dir, name, shots, vb, dist, onePx);
                        }
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

                        // ---- THE MIRROR'S FACE through the driver's eye (owner,
                        // 2026-10-17: "the reflective layer seems to interfere with
                        // the modeled area it is placed on"). The car and the eye
                        // move together in sub-millimetre steps - nothing moves on
                        // screen, only the world-space rounding of the cabin's
                        // vertices changes - so a pixel of the face that changes is
                        // the face and the housing plate 0.5 mm behind it trading
                        // places. The mirror's picture is rendered once, before.
                        Renderer faceR = null;
                        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                            if (r.sharedMaterial != null && r.sharedMaterial.name == "Cockpit_Mirror") { faceR = r; break; }
                        if (faceR != null)
                        {
                            player.transform.SetPositionAndRotation(carPos, carRot);
                            ci.PreviewMirror(cam);
                            var fwd = carRot * Vector3.forward; var rgt = carRot * Vector3.right;
                            var fc = faceR.bounds.center;
                            var eyeRel = (fc - fwd * 0.55f - rgt * 0.30f - Vector3.up * 0.04f) - carPos;
                            float nearWas2 = cam.nearClipPlane;
                            cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale);
                            ci.HideShell(true);
                            Color32[] first = null; int fw = 0, boxN = 0, faceFlips = 0;
                            var changed = new HashSet<int>();
                            for (int k = 0; k < 8; k++)
                            {
                                var dp = rgt * (k * 0.00037f) + fwd * (k * 0.00023f);
                                player.transform.SetPositionAndRotation(carPos + dp, carRot);
                                var e = carPos + dp + eyeRel;
                                var re = Quaternion.LookRotation(fc + dp - e);
                                string fn = "skydepth_" + name + "_mirrorface" + k;
                                PSXScreenshotTool.ShotAs(cam, fn, e, re);
                                var t = new Texture2D(2, 2);
                                t.LoadImage(File.ReadAllBytes(Path.Combine(dir, fn + ".png")));
                                // the face's inner 60% as a pixel box (the eye is where it was)
                                cam.transform.SetPositionAndRotation(e, re);
                                var fb3 = faceR.bounds;
                                float vx0 = 1f, vy0 = 1f, vx1 = 0f, vy1 = 0f;
                                for (int c8 = 0; c8 < 8; c8++)
                                {
                                    var wv = new Vector3((c8 & 1) == 0 ? fb3.min.x : fb3.max.x, (c8 & 2) == 0 ? fb3.min.y : fb3.max.y, (c8 & 4) == 0 ? fb3.min.z : fb3.max.z);
                                    var vp = cam.WorldToViewportPoint(wv);
                                    vx0 = Mathf.Min(vx0, vp.x); vx1 = Mathf.Max(vx1, vp.x); vy0 = Mathf.Min(vy0, vp.y); vy1 = Mathf.Max(vy1, vp.y);
                                }
                                float mx = (vx1 - vx0) * 0.2f, my = (vy1 - vy0) * 0.2f;
                                int px0 = Mathf.Clamp((int)((vx0 + mx) * t.width), 0, t.width - 1), px1 = Mathf.Clamp((int)((vx1 - mx) * t.width), 0, t.width - 1);
                                int py0 = Mathf.Clamp((int)((vy0 + my) * t.height), 0, t.height - 1), py1 = Mathf.Clamp((int)((vy1 - my) * t.height), 0, t.height - 1);
                                var px = t.GetPixels32();
                                if (first == null) { first = px; fw = t.width; boxN = (px1 - px0 + 1) * (py1 - py0 + 1); }
                                else if (t.width == fw)
                                    for (int y = py0; y <= py1; y++)
                                        for (int x = px0; x <= px1; x++)
                                        {
                                            int i = y * fw + x;
                                            int la = first[i].r + first[i].g + first[i].b, lb = px[i].r + px[i].g + px[i].b;
                                            if (Mathf.Abs(la - lb) > 72 && changed.Add(i)) faceFlips++;
                                        }
                                Object.DestroyImmediate(t);
                            }
                            ci.HideShell(false);
                            cam.nearClipPlane = nearWas2;
                            player.transform.SetPositionAndRotation(carPos, carRot);
                            Debug.Log($"[SkyDepth] {name} MIRROR FACE (driver's eye, car at {carPos.x:0},{carPos.z:0}): FLIPS {faceFlips} of {boxN} px ({100f * faceFlips / Mathf.Max(1, boxN):0.00}%) over 8 sub-mm steps");
                        }
                        else Debug.LogError("[SkyDepth] no Cockpit_Mirror renderer");
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

        /// <summary>The landmark's top (its parts from 120 m, their fins and
        /// the needle) as a viewport rect.</summary>
        static Rect CrownBox(Camera cam, List<CityMap.Footprint> parts, float floorY)
        {
            float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
            void Add(Vector3 w)
            {
                var v = cam.WorldToViewportPoint(w);
                if (v.z <= 0f) return;
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            foreach (var p in parts)
            {
                if (p.h < 120f) continue;
                foreach (var q in p.pts) { Add(new Vector3(q.x, floorY + Mathf.Max(p.minH, 120f), q.y)); Add(new Vector3(q.x, floorY + p.h + 14f, q.y)); }
                Add(new Vector3(p.centre.x, floorY + 265f, p.centre.y));
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>The CROWN lines: flips a/b (the depth) and pops over the
        /// sweep (pixels that turn on and off again) inside the crown's box,
        /// and Screenshots\skydepth_&lt;view&gt;_crown.png: a, b, the flips in
        /// magenta, then the four sweep frames, each x4.</summary>
        static void CrownScore(string dir, string name, List<string> shots, Rect vb, float dist, float onePx)
        {
            var tex = new List<Texture2D>();
            foreach (var s in shots) { var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(dir, s + ".png"))); tex.Add(t); }
            int w = tex[0].width, h = tex[0].height, pad = 6;
            int bx0 = Mathf.Clamp(Mathf.FloorToInt(vb.xMin * w) - pad, 0, w - 1), bx1 = Mathf.Clamp(Mathf.CeilToInt(vb.xMax * w) + pad, bx0 + 1, w);
            int by0 = Mathf.Clamp(Mathf.FloorToInt(vb.yMin * h) - pad, 0, h - 1), by1 = Mathf.Clamp(Mathf.CeilToInt(vb.yMax * h) + pad, by0 + 1, h);
            var px = new List<Color32[]>();
            foreach (var t in tex) px.Add(t.GetPixels32());
            int L(int f, int i) => px[f][i].r + px[f][i].g + px[f][i].b;
            int flips = 0, pops = 0, lit = 0;
            var flipMark = new HashSet<int>();
            for (int y = by0; y < by1; y++)
                for (int x = bx0; x < bx1; x++)
                {
                    int i = y * w + x;
                    int mx = 0;
                    for (int f = 0; f < px.Count; f++) mx = Mathf.Max(mx, L(f, i));
                    if (mx < 150) continue;
                    lit++;
                    if (Mathf.Abs(L(0, i) - L(1, i)) > 72) { flips++; flipMark.Add(i); }
                    int dirN = 0, turns = 0, last = L(2, i);
                    for (int f = 3; f < px.Count; f++)
                    {
                        int d = L(f, i) - last;
                        if (Mathf.Abs(d) <= 72) continue;
                        int sgn = d > 0 ? 1 : -1;
                        if (dirN != 0 && sgn != dirN) turns++;
                        dirN = sgn; last = L(f, i);
                    }
                    if (turns > 0) pops++;
                }
            Debug.Log($"[SkyDepth] {name} CROWN at {dist:0} m (1 px = {onePx:0.00} m), box {bx1 - bx0}x{by1 - by0} px: FLIPS {flips}, SWEEP POPS {pops} of {lit} drawn px");
            const int z = 4;
            int cw = (bx1 - bx0) * z, ch = (by1 - by0) * z;
            var sheet = new Texture2D(cw * 4 + 12, ch * 2 + 4, TextureFormat.RGB24, false);
            var bg = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(bg);
            void Tile(int f, int col, int row, bool mark)
            {
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int i = (by0 + y / z) * w + bx0 + x / z;
                        var c = mark && flipMark.Contains(i) ? new Color32(255, 0, 255, 255) : px[f][i];
                        sheet.SetPixel(col * (cw + 4) + x, (1 - row) * (ch + 4) + y, c);
                    }
            }
            Tile(0, 0, 0, false); Tile(1, 1, 0, false); Tile(0, 2, 0, true);
            for (int k = 0; k < 4 && k + 2 < px.Count; k++) Tile(k + 2, k, 1, false);
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "skydepth_" + name + "_crown.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            foreach (var t in tex) Object.DestroyImmediate(t);
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
