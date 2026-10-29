using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SKYLINE PASS (the owner, 2026-10-29: "The uptown skyline does not
    /// seem accurate", with a daytime aerial from the stadium side). Frames of
    /// the game against that photograph, at the game's own draw distance,
    /// ring, skyline, grade and HD lines:
    ///   match    the photo's pose (CrownFrameShots' "match": 800 m out on the
    ///            stadium's side, 95 m up, the open-frame tower's top at the
    ///            photograph's pixel, a 70 degree lens), 2000x1263
    ///   i77s16   I-77 1.6 km south at chase height, looking at uptown
    ///   tryon    S Tryon St at street level ~300 m south-west of the Square,
    ///            looking up the canyon
    /// PNGs: Screenshots\skym\skym_&lt;view&gt;_&lt;hour&gt;[_base|_clear][_tag].png,
    /// with skym_shots.txt (each camera, the hour's globals) and, for the
    /// match view, skym_towers_&lt;hour&gt;[_tag].txt: every tower in the frame
    /// with its OSM look, the facade the builder picks for it (look, tint,
    /// albedo: CityMeshes.FacadeProbe), whether its tile or a far copy draws
    /// it, and the rendered colour at its middle.
    ///   PSX_SKYM_SHOTS=match:noon,tryon:noon   (view:hour pairs; default below)
    ///   PSX_SKYM_TAG=before                     a suffix
    ///   PSX_SKYM_AB=1                           each shot twice: LookChoices.ClearDay off (_base) and on (_clear)
    ///   PSX_SKYM_RING=5                         the match view's ring of live tiles (default the game's)
    /// Headless: -executeMethod PSXRacing.EditorTools.SkylineMatchShots.Run
    /// </summary>
    public static class SkylineMatchShots
    {
        const int MatchW = 2000, MatchH = 1263;
        const float MatchX = 1365f, MatchY = 223f, MatchHfov = 70f;
        const string DefaultShots = "match:noon,match:afternoon,match:night,i77s16:noon,tryon:noon";

        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[SkylineMatch] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[SkylineMatch] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            if (player != null) player.SetActive(false);

            string tag = System.Environment.GetEnvironmentVariable("PSX_SKYM_TAG");
            tag = string.IsNullOrEmpty(tag) ? "" : "_" + tag;
            string shotsEnv = System.Environment.GetEnvironmentVariable("PSX_SKYM_SHOTS");
            var shots = new List<(string view, int hour)>();
            foreach (var s in (string.IsNullOrEmpty(shotsEnv) ? DefaultShots : shotsEnv).Split(','))
            {
                var p = s.Trim().Split(':');
                if (p.Length != 2) continue;
                int hi = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(p[1].Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (hi >= 0) shots.Add((p[0].Trim(), hi));
            }
            bool ab = System.Environment.GetEnvironmentVariable("PSX_SKYM_AB") == "1";
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_SKYM_RING") ?? "", out int ringOverride);
            // SETS (one Unity job for the whole sheet): PSX_SKYM_SETS =
            // "before=match:noon,match:afternoon;after=...;clear=..." - each set
            // its own world (the skyline and proxies are built with the set's
            // switches) and its own tag:
            //   before  the city as it was (CityMeshes.LookFixOff, no lot trees,
            //           PSX_FACDAY=0: the facades' old day light), the sky as shipped
            //   after   this pass, the sky as shipped
            //   clear   this pass with the sky A/B on (LookChoices.ClearDay)
            var sets = new List<(string name, List<(string, int)> shots)>();
            string setsEnv = System.Environment.GetEnvironmentVariable("PSX_SKYM_SETS");
            if (string.IsNullOrEmpty(setsEnv)) sets.Add((null, shots));
            else
                foreach (var part in setsEnv.Split(';'))
                {
                    var kv = part.Split('=');
                    if (kv.Length != 2) continue;
                    var list = new List<(string, int)>();
                    foreach (var s in kv[1].Split(','))
                    {
                        var p = s.Trim().Split(':');
                        if (p.Length != 2) continue;
                        int hi = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(p[1].Trim(), System.StringComparison.OrdinalIgnoreCase));
                        if (hi >= 0) list.Add((p[0].Trim(), hi));
                    }
                    sets.Add((kv[0].Trim(), list));
                }

            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var keepPixels = PSXQuality.Current;
            float keepFov = cam.fieldOfView;
            var keepForce = LookChoices.ClearDayForce;
            bool keepLookOff = CityMeshes.LookFixOff, keepLotTrees = CityTrees.LotTreesOn;
            string keepFacDay = System.Environment.GetEnvironmentVariable("PSX_FACDAY");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "skym");
            Directory.CreateDirectory(dir);
            GameObject go = null;
            CityWorld world = null;
            var log = new System.Text.StringBuilder();
            string baseTag = tag;

            // the open-frame tower: the match pose is set from its top
            int shaft = -1; float best = 0f;
            for (int i = 0; i < map.footprints.Length; i++)
            {
                var f = map.footprints[i];
                if (f.landmark != 2 || !f.part || f.minH > 1f || f.h < 150f) continue;
                if (f.hu * f.hv > best) { best = f.hu * f.hv; shaft = i; }
            }
            try
            {
              PSXQuality.Current = PSXPixels.HD;
              foreach (var set in sets)
              {
                if (set.name != null)
                {
                    bool before = set.name == "before";
                    CityMeshes.LookFixOff = before;
                    CityTrees.LotTreesOn = !before;
                    System.Environment.SetEnvironmentVariable("PSX_FACDAY", before ? "0" : null);
                    LookChoices.ClearDayForce = set.name == "clear";
                    tag = "_" + set.name + baseTag;
                    log.Append($"== set {set.name}: LookFixOff {CityMeshes.LookFixOff}, LotTrees {CityTrees.LotTreesOn}, FacDayOff {TimeOfDay.FacDayOff}, ClearDay {LookChoices.ClearDay}\n");
                }
                if (go != null) { world.DropAll(); Object.DestroyImmediate(go); }
                go = new GameObject("~skylineMatchWorld");
                world = go.AddComponent<CityWorld>();
                string lastView = null;
                foreach (var (view, hour) in set.shots)
                {
                    if (!Pose(map, view, shaft, out var eye, out var rot, out float vfov, out int dispW, out int dispH))
                    {
                        Debug.LogError($"[SkylineMatch] no pose for {view}");
                        continue;
                    }
                    if (view != lastView)
                    {
                        world.DropAll();
                        // (match5: the same pose with five tiles round the eye live -
                        // uptown's towers as tiles, not far copies: the A/B of the two)
                        world.EnsureRing(eye, view == "match5" ? 5 : view == "match" && ringOverride > 0 ? ringOverride : world.ring);
                        lastView = view;
                    }
                    int lines = PSXQuality.HDLines(dispH);
                    int w = CityFarShots.Even(Mathf.RoundToInt(lines * dispW / (float)dispH));
                    var variants = ab ? new bool?[] { false, true } : new bool?[] { null };
                    foreach (var force in variants)
                    {
                        if (ab) LookChoices.ClearDayForce = force;
                        // the camera's own aspect is the frame's (the tower table projects with it)
                        cam.aspect = w / (float)lines;
                        TimeOfDay.Apply(hour, sun);
                        NightGlow.PreviewAll(hour >= TimeOfDay.Dusk);
                        if (globals != null) globals.Apply();
                        cam.fieldOfView = vfov;
                        cam.transform.SetPositionAndRotation(eye, rot);
                        world.RefreshSkyline(cam);
                        var rt = new RenderTexture(w, lines, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                        rt.Create();
                        StreetLights.Push(cam.transform.position, cam.transform.forward);
                        PSXTexDecode.StampAll();
                        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                        var shown = PSXScreenshotTool.Dithered(rt);
                        var tex = CityFarShots.Read(shown);
                        string hn = TimeOfDay.All[hour].name.ToLowerInvariant();
                        string ver = force == null ? "" : force.Value ? "_clear" : "_base";
                        string ringTag = view == "match" && ringOverride > 0 ? "_ring" + ringOverride : "";
                        if (view.StartsWith("match"))
                            File.WriteAllText(Path.Combine(dir, $"skym_towers_{view}_{hn}{ver}{ringTag}{tag}.txt"), Towers(map, world, cam, tex, dispW, dispH));
                        var big = CityFarShots.PointTo(tex, dispW, dispH);
                        File.WriteAllBytes(Path.Combine(dir, $"skym_{view}_{hn}{ver}{ringTag}{tag}.png"), big.EncodeToPNG());
                        if (big != tex) Object.DestroyImmediate(big);
                        Object.DestroyImmediate(tex);
                        if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                        rt.Release(); Object.DestroyImmediate(rt);
                        log.Append($"{view} {hn}{ver}: {Globals()}\n");
                    }
                    var e = rot.eulerAngles;
                    log.Append($"{view}: eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) yaw {e.y:0.0} pitch {-(e.x > 180f ? e.x - 360f : e.x):0.0} vfov {vfov:0.0}, {w}x{lines} -> {dispW}x{dispH}, tiles {world.LiveTiles}, lot trees {LotTrees(world)}\n");
                }
                log.Append(CityMeshes.HeroTriReport() + "\n");
              }
            }
            finally
            {
                cam.ResetAspect();
                CityMeshes.LookFixOff = keepLookOff;
                CityTrees.LotTreesOn = keepLotTrees;
                System.Environment.SetEnvironmentVariable("PSX_FACDAY", keepFacDay);
                LookChoices.ClearDayForce = keepForce;
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                cam.fieldOfView = keepFov;
                if (world != null) world.DropAll();
                if (go != null) Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, $"skym_shots{baseTag}.txt"), log.ToString());
            Debug.Log("[SkylineMatch] DONE | " + log.ToString().Replace("\n", " | "));
        }

        /// <summary>The lot trees planted so far (CityTrees.PlantLots), then reset.</summary>
        static int LotTrees(CityWorld world)
        {
            int n = CityTrees.LotPlantedTotal;
            CityTrees.LotPlantedTotal = 0;
            return n;
        }

        static bool Pose(CityMap map, string view, int shaft, out Vector3 eye, out Quaternion rot, out float vfov, out int dispW, out int dispH)
        {
            eye = default; rot = Quaternion.identity; vfov = 50f; dispW = 2000; dispH = 1125;
            if (view == "match" || view == "match5")
            {
                if (shaft < 0) return false;
                var T = map.footprints[shaft].centre;
                float g = CityElevation.GroundY(map, T.x, T.y);
                float r = 280f * Mathf.Deg2Rad;
                var at = T + new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * 800f;
                eye = new Vector3(at.x, g + 95f, at.y);
                dispW = MatchW; dispH = MatchH;
                float fpx = (dispW * 0.5f) / Mathf.Tan(MatchHfov * 0.5f * Mathf.Deg2Rad);
                vfov = 2f * Mathf.Atan(dispH * 0.5f / fpx) * Mathf.Rad2Deg;
                var top = new Vector3(T.x, g + 240f, T.y) - eye;
                float yaw = Mathf.Atan2(top.x, top.z) * Mathf.Rad2Deg - Mathf.Atan((MatchX - dispW * 0.5f) / fpx) * Mathf.Rad2Deg;
                float elev = Mathf.Atan2(top.y, new Vector2(top.x, top.z).magnitude) * Mathf.Rad2Deg;
                float pitchUp = elev - Mathf.Atan((dispH * 0.5f - MatchY) / fpx) * Mathf.Rad2Deg;
                rot = Quaternion.Euler(-pitchUp, yaw, 0f);
                return true;
            }
            if (view == "i77s16" || view == "i277s10")
            {
                // CityFarShots' spots: I-77 1.6 km / I-277 1.0 km south of uptown, chase height
                string road = view == "i77s16" ? "I-77" : "I-277";
                float km = view == "i77s16" ? 1600f : 1000f;
                float bestD = float.MaxValue; int bei = -1; float bs = 0f;
                for (int ei = 0; ei < map.edges.Length; ei++)
                {
                    var e = map.edges[ei];
                    if (e.link || e.name != road) continue;
                    for (float s = 0f; s <= e.length; s += 10f)
                    {
                        var p = e.PointAt(s);
                        if (p.y > map.uptown.y) continue;
                        float d = Mathf.Abs(Vector2.Distance(p, map.uptown) - km);
                        if (d < bestD) { bestD = d; bei = ei; bs = s; }
                    }
                }
                if (bei < 0) return false;
                var be = map.edges[bei];
                var at = be.PointAt(bs);
                eye = new Vector3(at.x, be.YAt(bs) + 2.2f, at.y);
                rot = Quaternion.LookRotation(new Vector3(map.uptown.x, eye.y + 120f, map.uptown.y) - eye);
                vfov = 50f;
                return true;
            }
            if (view == "tryon")
            {
                // S Tryon St, ~300 m south-west of the Square, looking up it
                float bestD = float.MaxValue; int bei = -1; float bs = 0f;
                for (int ei = 0; ei < map.edges.Length; ei++)
                {
                    var e = map.edges[ei];
                    if (e.name == null || e.name.IndexOf("Tryon", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    for (float s = 0f; s <= e.length; s += 5f)
                    {
                        var p = e.PointAt(s);
                        var d2 = p - map.uptown;
                        if (d2.x > 0f || d2.y > 0f) continue;
                        float d = Mathf.Abs(d2.magnitude - 300f);
                        if (d < bestD) { bestD = d; bei = ei; bs = s; }
                    }
                }
                if (bei < 0) return false;
                var be = map.edges[bei];
                var at = be.PointAt(bs);
                eye = new Vector3(at.x, be.YAt(bs) + 1.6f, at.y);
                var look = new Vector3(map.uptown.x, eye.y, map.uptown.y) - eye;
                look.y = 0f;
                rot = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(-14f, 0f, 0f);
                vfov = 55f;
                return true;
            }
            return false;
        }

        static string Globals()
        {
            string V(string n) { var v = Shader.GetGlobalVector(n); return $"{n}=({v.x:0.000},{v.y:0.000},{v.z:0.000},{v.w:0.000})"; }
            string F(string n) => $"{n}={Shader.GetGlobalFloat(n):0.000}";
            return string.Join(" ", V("_PSXAmbient"), V("_PSXSkyAmbient"), V("_PSXLightColor"), V("_PSXLightDir"), V("_PSXFogColor"),
                               F("_PSXExposure"), F("_PSXAdapt"), F("_PSXToneOn"), F("_PSXSunModel"), V("_PSXPaintWorld"), F("_PSXFogNear"), F("_PSXFogFar"));
        }

        /// <summary>Every tower 45 m and more whose middle is in the frame:
        /// the builder's pick and the colour the frame shows there.</summary>
        static string Towers(CityMap map, CityWorld world, Camera cam, Texture2D tex, int dispW, int dispH)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("idx    x(disp) y(disp)  dist   h     src  part outl  lm hid st use mat colour    rh | facade pick | rendered sRGB (5x5 at the middle)\n");
            var rows = new List<(float x, string line)>();
            for (int i = 0; i < map.footprints.Length; i++)
            {
                var f = map.footprints[i];
                if (f.h < 45f || f.hidden || f.gable) continue;
                var gf = f.part && f.outline >= 0 ? map.footprints[f.outline] : f;
                float g = CityElevation.GroundY(map, gf.centre.x, gf.centre.y);
                var mid = new Vector3(f.centre.x, g + Mathf.Max(f.minH, 0f) * 0.5f + f.h * 0.5f, f.centre.y);
                var vp = cam.WorldToViewportPoint(mid);
                if (vp.z <= 1f || vp.z > 4500f || vp.x < 0.01f || vp.x > 0.99f || vp.y < 0.01f || vp.y > 0.99f) continue;
                int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * tex.width), 2, tex.width - 3);
                int py = Mathf.Clamp(Mathf.RoundToInt(vp.y * tex.height), 2, tex.height - 3);
                var cols = tex.GetPixels(px - 2, py - 2, 5, 5);
                Color m = Color.black;
                foreach (var c in cols) m += c;
                m /= cols.Length;
                bool live = world.TileLive(f.centre);
                var col = f.colour;
                string line = $"{i,-6} {vp.x * dispW,6:0} {(1f - vp.y) * dispH,6:0} {vp.z,6:0} {f.h,5:0} {(live ? "tile" : "far "),4} {(f.part ? "P" : "-"),4} {f.outline,5} {f.landmark,3} {(f.hidden ? "H" : "-"),3} {f.style,2} {f.use,3} {f.mat,3} " +
                              $"{(col.a == 255 ? $"#{col.r:x2}{col.g:x2}{col.b:x2}" : "  none "),9} {f.rhythm,2} | {CityMeshes.FacadeProbe(map, i)} | " +
                              $"({Mathf.RoundToInt(m.r * 255),3},{Mathf.RoundToInt(m.g * 255),3},{Mathf.RoundToInt(m.b * 255),3})";
                rows.Add((vp.x, line));
            }
            rows.Sort((a, b) => a.x.CompareTo(b.x));
            foreach (var r in rows) sb.Append(r.line).Append('\n');
            return sb.ToString();
        }
    }
}
