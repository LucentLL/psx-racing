using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RAIN 2's still frames and its GPU cost (tools\rain2-shots.ps1).
    ///
    /// Edit mode, Charlotte in the rain, fixed poses on one lit arterial with
    /// curbs: the chase view, a low look along the gutter, the cockpit parked /
    /// cruising / racing (the glass as it is at that speed; the car itself
    /// stands), the bonnet, and a road under a bridge. One process shoots ONE
    /// side: PSX_RAIN2=0 is the rain as it was, =1 the new one, so the pair is
    /// honest (same poses, fresh processes). Frames go to
    /// PSX_RAIN2_OUT\&lt;tag&gt;\r2_*.png with a log beside them.
    ///
    /// PSX_RAIN2_TIMING=1 adds the cost at PSX_RAIN2_TW x PSX_RAIN2_TH
    /// (3832 x 2054, the owner's display): each part switched off and on
    /// render by render in ONE process, every render drained (so the GPU's
    /// share is inside the clock), medians.
    /// </summary>
    public static class Rain2Shots
    {
        static StringBuilder log;
        static string outDir;
        static int W, H;

        static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        static int EnvInt(string n, int d) => int.TryParse(System.Environment.GetEnvironmentVariable(n), out int v) ? v : d;
        static void Line(string s) { log.AppendLine(s); Debug.Log("[Rain2Shots] " + s); }

        public static void Run()
        {
            log = new StringBuilder();
            string tag = Env("PSX_RAIN2_TAG", Rain2.On ? "on" : "off");
            outDir = Path.Combine(Env("PSX_RAIN2_OUT", Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "rain2")), tag);
            Directory.CreateDirectory(outDir);
            W = EnvInt("PSX_RAIN2_W", 1920); H = EnvInt("PSX_RAIN2_H", 1080);
            Line($"RAIN2 SHOTS {System.DateTime.Now:yyyy-MM-dd HH:mm}  tag {tag}  Rain2.On {Rain2.On}  tier {Rain2.CurrentTier}  {W}x{H}");
            int keepWeather = RaceHandoff.WeatherOverride;
            var keepView = ChaseCamera.Current;
            bool ok = false;
            try { ok = Capture(); }
            catch (System.Exception e) { Line("THREW " + e); }
            finally
            {
                Rain2Driver.PreviewClear();
                WeatherFx.PreviewClear();
                CockpitInterior.PreviewRain(0f, 0f, 0f);
                CockpitInterior.PreviewWipers(false, 0f, 0f, -1f);
                WetReflections.PreviewSet(null, -1);
                Shader.SetGlobalVector(PaintGloss.CarDropsId, Vector4.zero);
                RaceHandoff.WeatherOverride = keepWeather;
                ChaseCamera.PreviewView(keepView);
                Line(ok ? "RAIN2 SHOTS OK" : "RAIN2 SHOTS FAILED");
                File.WriteAllText(Path.Combine(outDir, "r2_log.txt"), log.ToString());
            }
        }

        static bool Capture()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
            if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Line("no Charlotte scene"); return false; }
            var world = Object.FindAnyObjectByType<CityWorld>();
            var map = CityMap.Get();
            if (world == null || map == null) { Line("no CityWorld / city data"); return false; }
            var sun = NightLookShots.FindSun();
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            var car = player.GetComponent<CarController>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            RaceHandoff.WeatherOverride = (int)Weather.Rain;

            // the spot: a lit two-way arterial near the 277 loop's start, level enough to hold water
            var loop = TrackCatalog.At(TrackCatalog.IndexOf("UptownLoop"));
            string routeId = loop.id == "UptownLoop" && !string.IsNullOrEmpty(loop.cityRoute) ? loop.cityRoute : "uptown";
            Vector2 start = NightLookShots.RouteStart(map, routeId, map.uptown);
            var cands = NightLookShots.Candidates(map, start, e => !e.link && !e.bridge && !e.tunnel && !e.oneway && (e.cls == 2 || e.cls == 3), 4000f);
            if (cands.Count == 0) { Line("no arterial"); return false; }
            (CityMap.Edge e, float s) spot = cands[0];
            foreach (var c in cands)
            {
                Vector2 p2 = c.e.PointAt(c.s);
                var at = new Vector3(p2.x, c.e.YAt(c.s), p2.y);
                world.EnsureRing(at, 0);
                float grade = Mathf.Abs(c.e.YAt(Mathf.Min(c.e.length, c.s + 10f)) - c.e.YAt(Mathf.Max(0f, c.s - 10f))) / 20f;
                if (StreetLights.CountWithin(at, 60f, StreetLights.Kind.Street) > 0 && grade < 0.02f) { spot = c; break; }
            }
            NightLookShots.Stand(spot.e, spot.s, out Vector3 pos, out Quaternion rot);
            world.EnsureRing(pos, 2);
            if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
            WetReflections.TagAll();
            Physics.SyncTransforms();
            var t = player.transform;
            Vector2 c2 = spot.e.PointAt(spot.s), tan = spot.e.TangentAt(spot.s).normalized;
            Vector3 centre = new Vector3(c2.x, spot.e.YAt(spot.s), c2.y), fwd = new Vector3(tan.x, 0f, tan.y), right = new Vector3(tan.y, 0f, -tan.x);
            Line($"spot '{spot.e.name}' cls {spot.e.cls} width {spot.e.width:0.0} m lanes {spot.e.lanes} at ({pos.x:0}, {pos.z:0}); " +
                 $"{StreetLights.CountWithin(pos, 60f, StreetLights.Kind.Street)} lamps within 60 m");

            // poses
            Vector3 chaseEye = t.position - t.forward * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - chaseEye);
            float half = spot.e.width * 0.5f;
            Vector3 curbEye = centre + right * (half - 1.5f) + Vector3.up * 0.55f - fwd * 2f;
            var curbRot = Quaternion.LookRotation(centre + right * (half - 0.35f) + fwd * 7f - curbEye);
            Vector3 bonnetEye = t.position + t.forward * 2.9f + t.right * 1.7f + Vector3.up * 1.75f;
            var bonnetRot = Quaternion.LookRotation(t.position + t.forward * 1.25f + Vector3.up * 0.72f - bonnetEye);

            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            int night = TimeOfDay.Night, day = TimeOfDay.Afternoon;

            foreach (int hour in new[] { night, day })
            {
                string hn = hour == night ? "night" : "day";
                Hour(hour, sun, globals);
                Rain(out float rain, out float level);
                Line($"[{hn}] wet {Shader.GetGlobalFloat("_PSXWetness"):0.00} water {Shader.GetGlobalFloat("_PSXWetWater"):0.00} precipRate {Seasons.Now.precipRate:0.00} -> rain {rain:0.00} level {level:0.00}");

                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                Take(cam, "chase_idle_" + hn, chaseEye, chaseRot, rain, level, 0f);
                Take(cam, "curb_" + hn, curbEye, curbRot, rain, level, 0f);
                if (hour == day) Take(cam, "bonnet_" + hn, bonnetEye, bonnetRot, rain, level, 0f);

                // the cockpit: parked, cruising (45 mph), racing (110 mph)
                var states = hour == night
                    ? new[] { ("idle", 0f, -1f), ("cruise", 20f, 0.42f), ("race", 49f, 0.30f) }
                    : new[] { ("idle", 0f, -1f), ("cruise", 20f, 0.42f) };
                foreach (var (sn, mps, into) in states)
                    Cockpit(cam, player, car, "cockpit_" + sn + "_" + hn, mps, into, rain, level);
                cam.fieldOfView = keepFov; cam.nearClipPlane = keepNear;
            }

            // under a bridge: no rings, no splashes there
            Hour(night, sun, globals);
            Rain(out float r2, out float l2);
            if (UnderBridge(map, world, start, out Vector3 uEye, out Quaternion uRot, out string uName))
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                world.EnsureRing(uEye, 2);
                WetReflections.TagAll();
                Physics.SyncTransforms();
                Take(cam, "underbridge_night", uEye, uRot, r2, l2, 0f);
                Line($"under bridge at '{uName}': covered cells {Rain2Driver.CoveredCells}, anchors {Rain2Driver.Anchors}");
            }
            else Line("no road under a bridge found near the start");

            // back at the spot: the pools' share of the road, on the CPU's port
            world.EnsureRing(pos, 2);
            Rain2.Push(r2, l2);
            int inPool = 0, onRoad = 0;
            for (float a = -40f; a <= 40f; a += 0.5f)
                for (float b = -half + 0.7f; b <= half - 0.7f; b += 0.5f)
                {
                    Vector3 q = centre + fwd * a + right * b;
                    if (!Physics.Raycast(q + Vector3.up * 3f, Vector3.down, out var hit, 8f, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                    onRoad++;
                    if (Rain2.PoolAt(hit.point, hit.normal) > 0.5f) inPool++;
                }
            Line($"pools on this road (CPU port, level {l2:0.00}): {inPool} of {onRoad} half-metre cells = {(onRoad > 0 ? 100f * inPool / onRoad : 0f):0.0} %");

            if (Env("PSX_RAIN2_TIMING", "0") == "1")
                Timing(cam, player, car, chaseEye, chaseRot, curbEye, curbRot, r2, l2);
            cam.fieldOfView = keepFov; cam.nearClipPlane = keepNear;
            return true;
        }

        static void Hour(int hour, Light sun, PSXGlobals globals)
        {
            TimeOfDay.Apply(hour, sun);
            if (globals != null) globals.Apply();
            var h = TimeOfDay.At(hour);
            NightGlow.PreviewAll(h.lightsOn);
            bool lit = h.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather);
            foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(lit);
            // the cars are wet (CarLights pushes this from the live rain; a preview has none)
            Shader.SetGlobalVector(PaintGloss.CarDropsId, new Vector4(1f, 0f, 0f, 0f));
        }

        /// <summary>The two numbers the game's driver would push for this weather.</summary>
        static void Rain(out float rain, out float level)
        {
            var wn = Seasons.Now;
            rain = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(wn.precipRate));
            level = Rain2.LevelFor(true, wn.precipRate, Shader.GetGlobalFloat("_PSXWetWater"));
        }

        static void Take(Camera cam, string name, Vector3 eye, Quaternion rot, float rain, float level, float camSpeed)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            WeatherFx.Preview(Weather.Rain, cam, 2.5f);
            Rain2Driver.Preview(cam, rain, level, camSpeed);
            CarLights.PushGlobals();
            var tex = Shoot(cam, W, H);
            File.WriteAllBytes(Path.Combine(outDir, "r2_" + name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Line($"  r2_{name}.png  anchors {Rain2Driver.Anchors} splashes {Rain2Driver.LiveSplashes} covered {Rain2Driver.CoveredCells} " +
                 $"_PSXRain2 {Shader.GetGlobalVector("_PSXRain2")} headlights {CarLights.PushedCount} lamps {StreetLights.PushedCount}");
        }

        static CockpitInterior Cabin(Camera cam, GameObject player, CarController car, float mps, float into, float rain, out Vector3 pos, out Quaternion rot)
        {
            var t = player.transform;
            var box = player.GetComponent<BoxCollider>();
            var body = player.GetComponent<CarBody>();
            Vector3 c = box != null ? box.center : new Vector3(0f, 0.72f, 0.05f);
            Vector3 s = box != null ? box.size : new Vector3(1.72f, 1.0f, 4.1f);
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            var cv = Object.FindFirstObjectByType<CockpitView>();
            if (car != null) { car.currentRPM = 900f + mps * 90f; car.speedKmh = mps * 3.6f; }
            if (cv != null) cv.PreviewShow(true);
            var ci = player.GetComponentInChildren<CockpitInterior>(true);
            pos = Vector3.zero; rot = Quaternion.identity;
            if (ci == null) return null;
            ci.Refit();
            ci.Show(true, true);
            ci.HideShell(true);
            ci.SetSteer(0f, car != null ? car.maxSteerLowSpeedDeg : 34f);
            ci.Tick(0f, null);
            // after the tick (it pushes the live clocks): the glass as it is at this speed
            CockpitInterior.PreviewRain(1f, mps, 37f);
            CockpitInterior.PreviewWipers(true, rain, mps, into);
            Vector3 eye = ChaseCamera.MountOffset(ChaseCamera.View.Cockpit, c, s, body != null ? body.Def : null,
                                                  body != null ? body.LengthScale : 1f, body != null ? body.BodyLift : 0f, false);
            pos = t.TransformPoint(eye);
            rot = t.rotation * Quaternion.Euler(ChaseCamera.MountPitch(ChaseCamera.View.Cockpit) + CockpitLook.DownFor(0f), 0f, 0f);
            cam.fieldOfView = ChaseCamera.FOVFor(ChaseCamera.View.Cockpit, 58f, 0f, 1f, 16f / 9f);
            cam.nearClipPlane = CockpitInterior.NearClipFor(ci.Current.scale, 0f);
            return ci;
        }

        static void Cockpit(Camera cam, GameObject player, CarController car, string name, float mps, float into, float rain, float level)
        {
            DriveSide.Override = false;
            var ci = Cabin(cam, player, car, mps, into, rain, out Vector3 pos, out Quaternion rot);
            if (ci == null) { Line("  no CockpitInterior on the player: " + name + " skipped"); return; }
            Take(cam, name, pos, rot, rain, level, mps);
            Line($"    {name}: {mps * 2.237f:0} mph, wipers {(into < 0f ? "parked" : into.ToString("0.00") + " s into the stroke")} " +
                 $"_CabinWipe {Shader.GetGlobalVector("_CabinWipe")} _CabinRain2 {Shader.GetGlobalVector("_CabinRain2")} live {Shader.GetGlobalFloat("_PSXSSRLive")}");
            ci.HideShell(false);
            ci.Show(false, false);
            var cv = Object.FindFirstObjectByType<CockpitView>();
            if (cv != null) cv.PreviewShow(false);
            if (car != null) { car.speedKmh = 0f; }
            DriveSide.Override = null;
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
        }

        /// <summary>A point on a street that runs under a bridge near
        /// <paramref name="near"/>, the eye 14 m short of the deck's edge.</summary>
        static bool UnderBridge(CityMap map, CityWorld world, Vector2 near, out Vector3 eye, out Quaternion rot, out string name)
        {
            eye = Vector3.zero; rot = Quaternion.identity; name = null;
            var segs = new HashSet<int>();
            float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e == null || e.pts == null || e.pts.Length < 2 || e.bridge || e.tunnel || e.link || e.length < 80f || e.cls < 1 || e.cls > 3) continue;
                if (Vector2.Distance(e.pts[0], near) > 5000f + e.length) continue;
                for (float s = 30f; s <= e.length - 30f; s += 6f)
                {
                    Vector2 p = e.PointAt(s);
                    float d = Vector2.Distance(p, near);
                    if (d >= best || e.ElevatedAt(s)) continue;
                    float y = e.YAt(s);
                    segs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * 5f, p + Vector2.one * 5f, segs);
                    bool under = false;
                    foreach (int packed in segs)
                    {
                        var o = map.edges[packed >> 12];
                        if (o == e || !o.bridge) continue;
                        int si = packed & 0xFFF;
                        Vector2 a = o.pts[si], b = o.pts[si + 1], dd = b - a;
                        float L2 = dd.sqrMagnitude;
                        float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, dd) / L2) : 0f;
                        if (Vector2.Distance(p, a + dd * tt) > o.width * 0.4f) continue;
                        if (o.YAt(o.s[si] + Mathf.Sqrt(L2) * tt) > y + 4f) { under = true; break; }
                    }
                    if (!under) continue;
                    best = d;
                    Vector2 tan = e.TangentAt(s).normalized;
                    Vector3 f = new Vector3(tan.x, 0f, tan.y);
                    float sb = Mathf.Max(0f, s - 16f);
                    Vector2 pb = e.PointAt(sb);
                    eye = new Vector3(pb.x, e.YAt(sb) + 1.6f, pb.y) + new Vector3(tan.y, 0f, -tan.x) * (e.oneway ? 0f : e.width * 0.2f);
                    rot = Quaternion.LookRotation(new Vector3(p.x, y + 0.3f, p.y) + f * 6f - eye);
                    name = e.name;
                }
            }
            return name != null;
        }

        // ------------------------------------------------------------------
        //  rendering
        // ------------------------------------------------------------------
        static void Submit(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            PSXTexDecode.StampAll();
            RenderPipeline.SubmitRenderRequest(cam, request);
        }

        static Texture2D Shoot(Camera cam, int w, int h)
        {
            WetReflections.PreviewSet(cam, 1);
            var rt = new RenderTexture(w, h, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            Submit(cam, rt);
            var shown = PSXScreenshotTool.Dithered(rt);
            var prev = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static void Drain(RenderTexture rt, Texture2D one)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = prev;
        }

        static double Median(List<double> v)
        {
            v.Sort();
            return v.Count == 0 ? 0.0 : (v.Count & 1) == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        /// <summary>Median render time with <paramref name="set"/>(false) and
        /// (true), alternating render by render, each drained.</summary>
        static (double off, double on) Alternate(Camera cam, int w, int h, int n, System.Action<bool> set)
        {
            var rt = new RenderTexture(w, h, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            WetReflections.PreviewSet(cam, 1);
            for (int i = 0; i < 8; i++) { set((i & 1) == 1); Submit(cam, rt); Drain(rt, one); }
            var offs = new List<double>();
            var ons = new List<double>();
            var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 2 * n; i++)
            {
                bool on = (i & 1) == 1;
                set(on);
                sw.Restart();
                Submit(cam, rt);
                Drain(rt, one);
                sw.Stop();
                (on ? ons : offs).Add(sw.Elapsed.TotalMilliseconds);
            }
            Object.DestroyImmediate(one);
            rt.Release();
            Object.DestroyImmediate(rt);
            return (Median(offs), Median(ons));
        }

        static void Timing(Camera cam, GameObject player, CarController car, Vector3 chaseEye, Quaternion chaseRot,
                           Vector3 curbEye, Quaternion curbRot, float rain, float level)
        {
            if (!Rain2.On) { Line("TIMING skipped: this process has PSX_RAIN2=0"); return; }
            int tw = EnvInt("PSX_RAIN2_TW", 3832), th = EnvInt("PSX_RAIN2_TH", 2054), n = EnvInt("PSX_RAIN2_TN", 40);
            Line($"TIMING at {tw}x{th}, {n} renders a side, drained per render, medians (ms). GPU {SystemInfo.graphicsDeviceName}");
            var r2Id = Shader.PropertyToID("_PSXRain2");
            var wipeId = Shader.PropertyToID("_CabinWipe");
            var glassId = Shader.PropertyToID("_CabinRain2");

            void Row(string what, System.Action<bool> set)
            {
                var (off, on) = Alternate(cam, tw, th, n, set);
                Line($"  {what}: off {off:0.00}  on {on:0.00}  cost {on - off:+0.00;-0.00}");
            }
            void Splashes(bool on)
            {
                foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
                    if (ps.name.StartsWith("Rain2 ")) ps.enabled = on;
            }

            foreach (var (pn, eye, rot) in new[] { ("chase night", chaseEye, chaseRot), ("curb night (road fills the frame)", curbEye, curbRot) })
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                cam.transform.SetPositionAndRotation(eye, rot);
                WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                Rain2Driver.Preview(cam, rain, level, 0f);
                CarLights.PushGlobals();
                Vector4 full = Shader.GetGlobalVector(r2Id);
                Line($" pose {pn}: _PSXRain2 {full}, splashes {Rain2Driver.LiveSplashes}");
                Splashes(true);
                Row("ground, pools only (shader)", on => Shader.SetGlobalVector(r2Id, on ? new Vector4(full.x, 0f, full.z, 0f) : Vector4.zero));
                Row("ground, pools + rings (shader)", on => Shader.SetGlobalVector(r2Id, on ? full : Vector4.zero));
                Shader.SetGlobalVector(r2Id, full);
                Row("splash particles (crowns + rings)", Splashes);
                Row("WHOLE ground package", on => { Shader.SetGlobalVector(r2Id, on ? full : Vector4.zero); Splashes(on); });
                Shader.SetGlobalVector(r2Id, full); Splashes(true);
            }

            foreach (var (sn, mps, into) in new[] { ("cockpit parked", 0f, -1f), ("cockpit cruise 45 mph", 20f, 0.42f) })
            {
                DriveSide.Override = false;
                var ci = Cabin(cam, player, car, mps, into, rain, out Vector3 pos, out Quaternion rot);
                if (ci == null) { Line("  no cabin"); break; }
                cam.transform.SetPositionAndRotation(pos, rot);
                WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                Rain2Driver.Preview(cam, rain, level, mps);
                CarLights.PushGlobals();
                Vector4 full = Shader.GetGlobalVector(r2Id), wipe = Shader.GetGlobalVector(wipeId), glass = Shader.GetGlobalVector(glassId);
                Line($" pose {sn}: _CabinWipe {wipe} _CabinRain2 {glass}");
                Row("glass: wipers", on => Shader.SetGlobalVector(wipeId, on ? wipe : Vector4.zero));
                Row("glass: look-through + fine beads", on => Shader.SetGlobalVector(glassId, on ? glass : Vector4.zero));
                Row("WHOLE package (ground + splashes + glass)", on =>
                {
                    Shader.SetGlobalVector(r2Id, on ? full : Vector4.zero);
                    Splashes(on);
                    Shader.SetGlobalVector(wipeId, on ? wipe : Vector4.zero);
                    Shader.SetGlobalVector(glassId, on ? glass : Vector4.zero);
                });
                Shader.SetGlobalVector(r2Id, full); Splashes(true);
                Shader.SetGlobalVector(wipeId, wipe); Shader.SetGlobalVector(glassId, glass);
                ci.HideShell(false);
                ci.Show(false, false);
                var cv = Object.FindFirstObjectByType<CockpitView>();
                if (cv != null) cv.PreviewShow(false);
                DriveSide.Override = null;
            }
        }
    }
}
