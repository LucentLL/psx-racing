using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OPEN-FRAME CROWN (the owner, 2026-10-29: "a strange glass/light
    /// layer in triangles on top"). Close views of the open-frame tower's
    /// crown (landmark 2) from the west - the stadium side, the notch between
    /// its two west faces - and from the south-west (its south-west face
    /// square on), by day and by night; and the skyline from the stadium side
    /// at roughly the pose of the owner's aerial reference photograph (the
    /// tower ~14 deg right of the centre, its top ~1/6 down a 2000x1263 frame,
    /// a ~70 deg lens, 800 m out on the stadium's side).
    /// PNGs: Screenshots\crown\crown_&lt;view&gt;_&lt;hour&gt;[_tag].png, where each
    /// camera stood in crown_shots.txt.
    ///   PSX_CROWN_TAG=before          a suffix
    ///   PSX_CROWN_VIEWS=w,sw,match     (default all)
    /// Headless: -executeMethod PSXRacing.EditorTools.CrownFrameShots.Run
    /// </summary>
    public static class CrownFrameShots
    {
        struct View
        {
            public string name;
            public float brg, dist, eyeUp;     // compass bearing and metres from the tower, metres over its ground
            public float lookUp, vfov;         // close views: the point looked at over the tower's ground; vertical fov
            public bool match;                 // the photo pose: the tower top put at (MatchX, MatchY) of the frame
            public bool far;                   // the game's ring round the eye (the tower from the far skyline)
            public int[] hours;
        }

        // the reference photograph's frame and where the tower's top sits in it
        const int MatchW = 2000, MatchH = 1263;
        const float MatchX = 1365f, MatchY = 223f, MatchHfov = 70f;

        static readonly View[] Views =
        {
            new View { name = "w",  brg = 285f, dist = 380f, eyeUp = 190f, lookUp = 207f, vfov = 22f, hours = new[] { TimeOfDay.Noon, TimeOfDay.Night } },
            new View { name = "sw", brg = 225f, dist = 380f, eyeUp = 165f, lookUp = 207f, vfov = 22f, hours = new[] { TimeOfDay.Noon, TimeOfDay.Night } },
            // the whole tower from the south-west near the ground, as the
            // owner's night photograph has it (its far skyline copy)
            new View { name = "swfar", brg = 235f, dist = 1400f, eyeUp = 30f, lookUp = 135f, vfov = 14f, far = true, hours = new[] { TimeOfDay.Night } },
            // ...and from the east-north-east (the other notch, the other faces)
            new View { name = "efar", brg = 70f, dist = 1400f, eyeUp = 30f, lookUp = 135f, vfov = 14f, far = true, hours = new[] { TimeOfDay.Night } },
            // the photo's eye: the stadium and the spired tower on one bearing,
            // the far horizon ~490 px down (the first try at 50 m sat ~90 px low)
            new View { name = "match", brg = 280f, dist = 800f, eyeUp = 95f, match = true, hours = new[] { TimeOfDay.Noon, TimeOfDay.Night } },
        };

        public static void Run()
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogError("[CrownShots] Charlotte.unity did not open"); return; }
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CrownShots] no city data"); return; }
            PSXRacingBuilder.EnsureCityTextures();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            if (player != null) player.SetActive(false);

            // the tower: its shaft, the biggest of landmark 2's parts that stand on the ground
            int shaft = -1; float best = 0f;
            for (int i = 0; i < map.footprints.Length; i++)
            {
                var f = map.footprints[i];
                if (f.landmark != 2 || !f.part || f.minH > 1f || f.h < 150f) continue;
                if (f.hu * f.hv > best) { best = f.hu * f.hv; shaft = i; }
            }
            if (shaft < 0) { Debug.LogError("[CrownShots] no open-frame tower in the data"); return; }
            var T = map.footprints[shaft].centre;
            float g = CityElevation.GroundY(map, T.x, T.y);

            string tag = System.Environment.GetEnvironmentVariable("PSX_CROWN_TAG");
            tag = string.IsNullOrEmpty(tag) ? "" : "_" + tag;
            var only = new HashSet<string>((System.Environment.GetEnvironmentVariable("PSX_CROWN_VIEWS") ?? "").Split(','));
            only.Remove("");
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            int oldWeather = RaceHandoff.WeatherOverride;
            RaceHandoff.WeatherOverride = 0;
            var keepPixels = PSXQuality.Current;
            float keepFov = cam.fieldOfView;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "crown");
            Directory.CreateDirectory(dir);
            var go = new GameObject("~crownShotsWorld");
            var world = go.AddComponent<CityWorld>();
            var log = new System.Text.StringBuilder();
            log.Append($"tower shaft footprint {shaft} at ({T.x:0.0},{T.y:0.0}), ground {g:0.0}\n");
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                foreach (var v in Views)
                {
                    if (only.Count > 0 && !only.Contains(v.name)) continue;
                    float r = v.brg * Mathf.Deg2Rad;
                    var at = T + new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * v.dist;
                    var eye = new Vector3(at.x, g + v.eyeUp, at.y);
                    int dispW = v.match ? MatchW : 2000, dispH = v.match ? MatchH : 1125;
                    Quaternion rot;
                    float vfov;
                    if (v.match)
                    {
                        // the frame's focal length in display pixels, and the
                        // tower top's offset from the centre in it
                        float fpx = (dispW * 0.5f) / Mathf.Tan(MatchHfov * 0.5f * Mathf.Deg2Rad);
                        vfov = 2f * Mathf.Atan(dispH * 0.5f / fpx) * Mathf.Rad2Deg;
                        var top = new Vector3(T.x, g + 240f, T.y) - eye;
                        float yaw = Mathf.Atan2(top.x, top.z) * Mathf.Rad2Deg - Mathf.Atan((MatchX - dispW * 0.5f) / fpx) * Mathf.Rad2Deg;
                        float elev = Mathf.Atan2(top.y, new Vector2(top.x, top.z).magnitude) * Mathf.Rad2Deg;
                        float pitchUp = elev - Mathf.Atan((dispH * 0.5f - MatchY) / fpx) * Mathf.Rad2Deg;
                        rot = Quaternion.Euler(-pitchUp, yaw, 0f);
                        world.EnsureRing(eye, world.ring);
                    }
                    else
                    {
                        vfov = v.vfov;
                        rot = Quaternion.LookRotation(new Vector3(T.x, g + v.lookUp, T.y) - eye);
                        // the tower's own tile live (its near copy, not the skyline's),
                        // or the game's ring round a far eye
                        if (v.far) world.EnsureRing(eye, world.ring);
                        else world.EnsureRing(new Vector3(T.x, g, T.y), 1);
                    }
                    int lines = PSXQuality.HDLines(dispH);
                    int w = CityFarShots.Even(Mathf.RoundToInt(lines * dispW / (float)dispH));
                    foreach (int hour in v.hours)
                    {
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
                        var big = CityFarShots.PointTo(tex, dispW, dispH);
                        string file = $"crown_{v.name}_{TimeOfDay.All[hour].name.ToLowerInvariant()}{tag}.png";
                        File.WriteAllBytes(Path.Combine(dir, file), big.EncodeToPNG());
                        if (big != tex) Object.DestroyImmediate(big);
                        Object.DestroyImmediate(tex);
                        if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                        rt.Release(); Object.DestroyImmediate(rt);
                    }
                    var e = rot.eulerAngles;
                    log.Append($"{v.name}: eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) bearing {v.brg:0} deg {v.dist:0} m from the tower, {v.eyeUp:0} m over its ground, yaw {e.y:0.0} pitch {-(e.x > 180f ? e.x - 360f : e.x):0.0}, vfov {vfov:0.0}, {w}x{lines} -> {dispW}x{dispH}, tiles {world.LiveTiles}\n");
                    world.DropAll();
                }
                log.Append(CityMeshes.HeroTriReport() + "\n");
            }
            finally
            {
                PSXQuality.Current = keepPixels;
                RaceHandoff.WeatherOverride = oldWeather;
                cam.fieldOfView = keepFov;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "crown_shots.txt"), log.ToString());
            Debug.Log("[CrownShots] DONE | " + log.ToString().Replace("\n", " | "));
        }
    }
}
