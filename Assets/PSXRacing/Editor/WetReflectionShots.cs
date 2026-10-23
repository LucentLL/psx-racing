using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WORLD IN THE WATER, MEASURED (WetReflections, 2026-10-17). A rainy
    /// afternoon in each venue of PSX_WET_VENUES (scene names, default
    /// Neighborhood,BlueRidge; Charlotte works too), drawn at PSX_WET_LINES
    /// (default 1080) lines 16:9 through PSX/Blit, as Screenshots\:
    ///   ssr_&lt;venue&gt;_&lt;view&gt;_off.png   the road mirroring the sky alone (as live)
    ///   ssr_&lt;venue&gt;_&lt;view&gt;_on.png    the screen-space reflections
    /// for a chase view and the chase view again 2 m further forward
    /// (chase_fwd2); in the neighbourhood also up a neighbour's drive at its
    /// garage and again 2 m nearer (drive, drive_fwd2) - a house's reflection
    /// has to stay under the house; and the chase view at night.
    /// [WetShot] lines: wet renderers tagged, the hour's wetness, the share of
    /// pixels the mirror changed, and the camera's render TIMED off / on
    /// (PSX_WET_FRAMES renders each, default 40, alternating, the GPU drained
    /// after every render so its work is inside the clock; medians).
    /// Headless (with graphics; -force-glcore matches WebGL):
    /// -executeMethod PSXRacing.EditorTools.WetReflectionShots.Run
    /// </summary>
    public static class WetReflectionShots
    {
        public static void Run()
        {
            string venues = Env("PSX_WET_VENUES", "Neighborhood,BlueRidge");
            int lines = Mathf.Clamp(EnvInt("PSX_WET_LINES", 1080), 120, 2160);
            int width = Even(Mathf.RoundToInt(lines * 16f / 9f));
            int frames = Mathf.Max(5, EnvInt("PSX_WET_FRAMES", 40));
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(dir);
            Debug.Log($"[WetShot] graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}), {width}x{lines}");

            int oldWeather = RaceHandoff.WeatherOverride;
            var keepPixels = PSXQuality.Current;
            try
            {
                PSXQuality.Current = PSXPixels.HD;
                foreach (var v in venues.Split(','))
                {
                    string venue = v.Trim();
                    if (venue.Length == 0) continue;
                    try { Venue(venue, dir, width, lines, frames); }
                    catch (System.Exception e) { Debug.LogError("[WetShot] " + venue + ": " + e); }
                }
            }
            finally
            {
                WetReflections.PreviewSet(null, -1);
                RaceHandoff.WeatherOverride = oldWeather;
                PSXQuality.Current = keepPixels;
            }
            Debug.Log("[WetShot] DONE");
        }

        static void Venue(string venue, string dir, int width, int lines, int frames)
        {
            string path = "Assets/PSXRacing/Scenes/" + venue + ".unity";
            if (!File.Exists(path)) { Debug.LogError("[WetShot] missing scene " + path); return; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var cam = GameObject.Find("PSXCamera")?.GetComponent<Camera>();
            var car = GameObject.Find("RX-7 Player");
            if (car == null) car = Object.FindFirstObjectByType<CarController>()?.gameObject;
            if (cam == null || car == null) { Debug.LogError("[WetShot] " + venue + ": no PSXCamera or car"); return; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) c.enabled = false;
            foreach (var cv in Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None)) cv.PreviewShow(false);
            CarReflections.Off();
            SunGlareFx.PreviewSet(null, 0f, 0f, true);

            // the eye: down the street / road, behind the car
            Vector3 eye, fwd;
            GameObject worldGo = null;
            CityWorld world = null;
            if (venue == "Neighborhood")
            {
                var street = GameObject.Find("NbStreet")?.GetComponent<Renderer>();
                if (street == null) { Debug.LogError("[WetShot] no NbStreet"); return; }
                var b = street.bounds;
                // the near end of the street, looking along it toward the far end
                // (the houses either side), a lane right of the middle
                fwd = Vector3.back;
                var at = new Vector3(b.center.x + 1.3f, 0f, b.max.z - 40f);
                at.y = GroundY(at, b.max.y);
                PlaceCar(car, at + fwd * 6.5f, fwd, b.max.y);
                eye = at + Vector3.up * 2.0f;
            }
            else if (venue == "Charlotte")
            {
                var map = CityMap.Get();
                if (map == null) { Debug.LogError("[WetShot] no city data"); return; }
                PSXRacingBuilder.EnsureCityTextures();
                worldGo = new GameObject("~wetShotWorld");
                world = worldGo.AddComponent<CityWorld>();
                // the residential street nearest a point 2.5 km from the core
                float best = float.MaxValue; int bei = -1; float bs = 0f;
                for (int ei = 0; ei < map.edges.Length; ei++)
                {
                    var e = map.edges[ei];
                    if (e.link || e.length < 80f || e.cls > 1) continue;   // 0 local, 1 residential-ish
                    for (float s = 30f; s <= e.length - 30f; s += 10f)
                    {
                        float d = Mathf.Abs(Vector2.Distance(e.PointAt(s), map.uptown) - 2500f);
                        if (d < best) { best = d; bei = ei; bs = s; }
                    }
                }
                if (bei < 0) { Debug.LogError("[WetShot] no street"); return; }
                var be = map.edges[bei];
                var p = be.PointAt(bs);
                var t2 = be.TangentAt(bs).normalized;
                fwd = new Vector3(t2.x, 0f, t2.y);
                var carPos = new Vector3(p.x, be.YAt(bs), p.y);
                car.SetActive(true);
                car.transform.SetPositionAndRotation(carPos, Quaternion.LookRotation(fwd, Vector3.up));
                eye = carPos - fwd * 6.5f + Vector3.up * 2.0f;
                world.EnsureRing(eye, world.ring);
                Debug.Log($"[WetShot] Charlotte street '{be.name}' cls {be.cls}");
            }
            else
            {
                var t = car.transform;
                fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                eye = t.position - fwd * 6.5f + Vector3.up * 2.0f;
            }

            // the hour: a rainy afternoon
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            RaceHandoff.WeatherOverride = (int)Weather.Rain;
            TimeOfDay.Apply(TimeOfDay.Afternoon, sun);
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            int tagged = WetReflections.TagAll();
            Debug.Log($"[WetShot] {venue}: {tagged} wet renderer(s), wetness {Shader.GetGlobalFloat("_PSXWetness"):0.00}");

            // the views: the chase eye and the same eye 2 m forward; in the
            // neighbourhood also up a neighbour's wet drive at its garage (a
            // house standing at the end of wet tarmac) and again 2 m nearer;
            // and the chase eye at night (clear nights are damp too)
            var look = Quaternion.LookRotation(eye + fwd * 20f - Vector3.up * 1.0f - eye);
            int afternoon = TimeOfDay.Afternoon;
            int night = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals("night", System.StringComparison.OrdinalIgnoreCase));
            var views = new List<(string name, Vector3 eye, Quaternion rot, int hour)>
            {
                ("chase", eye, look, afternoon),
                ("chase_fwd2", eye + fwd * 2f, look, afternoon),
            };
            if (venue == "Neighborhood" && DriveView(out var dEye, out var dFwd))
            {
                var dLook = Quaternion.LookRotation(dFwd * 14f - Vector3.up * 1.1f);
                views.Add(("drive", dEye, dLook, afternoon));
                views.Add(("drive_fwd2", dEye + dFwd * 2f, dLook, afternoon));
            }
            if (night >= 0) views.Add(("night", eye, look, night));
            int hourNow = afternoon;
            foreach (var view in views)
            {
                if (view.hour != hourNow)
                {
                    hourNow = view.hour;
                    TimeOfDay.Apply(hourNow, sun);
                    if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                }
                if (world != null) { world.EnsureRing(view.eye, world.ring); WetReflections.TagAll(); }
                cam.transform.SetPositionAndRotation(view.eye, view.rot);
                var off = Shoot(cam, width, lines, 0);
                var on = Shoot(cam, width, lines, 1);
                File.WriteAllBytes(Path.Combine(dir, $"ssr_{venue}_{view.name}_off.png"), off.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(dir, $"ssr_{venue}_{view.name}_on.png"), on.EncodeToPNG());
                Debug.Log($"[WetShot] {venue} {view.name}: eye {view.eye} wetness {Shader.GetGlobalFloat("_PSXWetness"):0.00} - the mirror changed {Changed(off, on) * 100f:0.0}% of the pixels");
                Object.DestroyImmediate(off);
                Object.DestroyImmediate(on);
            }
            if (hourNow != afternoon)
            {
                TimeOfDay.Apply(afternoon, sun);
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            }

            // the cost, at the chase eye: each render drained on its own (so
            // the GPU's share is inside the clock, not hidden behind the CPU's),
            // off and on alternating render by render, medians
            cam.transform.SetPositionAndRotation(eye, look);
            if (world != null) world.EnsureRing(eye, world.ring);
            var (offMs, onMs) = TimeAlternating(cam, width, lines, frames);
            Debug.Log($"[WetShot] {venue} TIMING {width}x{lines}, {frames} renders each, drained per render: off {offMs:0.00} ms, on {onMs:0.00} ms (medians) - the mirror costs {onMs - offMs:0.00} ms a frame");

            if (worldGo != null) Object.DestroyImmediate(worldGo);
        }

        /// <summary>A neighbour's drive (the screenshot tool's NbDrive slabs):
        /// the eye on the street 4 m short of its kerb end, looking up it.</summary>
        static bool DriveView(out Vector3 eye, out Vector3 fwd)
        {
            eye = fwd = Vector3.zero;
            var street = GameObject.Find("NbStreet")?.GetComponent<Renderer>();
            if (street == null) return false;
            float streetX = street.bounds.center.x;
            for (int p = 0; p < 40; p++)
            {
                var r = GameObject.Find("NbDrive" + p)?.GetComponent<Renderer>();
                if (r == null) continue;
                var db = r.bounds;
                int side = db.center.x >= streetX ? 1 : -1;
                float kerbX = side > 0 ? db.min.x : db.max.x;
                var at = new Vector3(kerbX - side * 4f, 0f, db.center.z);
                at.y = GroundY(at, db.max.y + 2f);
                eye = at + Vector3.up * 1.6f;
                fwd = new Vector3(side, 0f, 0f);
                Debug.Log($"[WetShot] drive NbDrive{p}: {db.size.x:0.0} m long, side {side}");
                return true;
            }
            return false;
        }

        static float GroundY(Vector3 at, float top)
        {
            if (Physics.Raycast(new Vector3(at.x, top + 50f, at.z), Vector3.down, out var hit, 200f)) return hit.point.y;
            return top;
        }

        static void PlaceCar(GameObject car, Vector3 at, Vector3 fwd, float top)
        {
            at.y = GroundY(at, top);
            car.SetActive(true);
            car.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
        }

        /// <summary>One frame, as the game shows it, with the mirror off (0) or on (1).</summary>
        static Texture2D Shoot(Camera cam, int w, int h, int mode)
        {
            WetReflections.PreviewSet(cam, mode);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
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

        static void Submit(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            PSXTexDecode.StampAll();
            RenderPipeline.SubmitRenderRequest(cam, request);
        }

        /// <summary>The share of pixels that differ by more than 2 codes in any channel.</summary>
        static float Changed(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            int n = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) > 2 || Mathf.Abs(pa[i].g - pb[i].g) > 2 || Mathf.Abs(pa[i].b - pb[i].b) > 2) n++;
            }
            return n / (float)Mathf.Max(pa.Length, 1);
        }

        static (float off, float on) TimeAlternating(Camera cam, int w, int h, int n)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            for (int i = 0; i < 6; i++) { WetReflections.PreviewSet(cam, i & 1); Submit(cam, rt); Drain(rt, one); }
            var offs = new List<double>();
            var ons = new List<double>();
            var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 2 * n; i++)
            {
                int mode = i & 1;
                WetReflections.PreviewSet(cam, mode);
                sw.Restart();
                Submit(cam, rt);
                Drain(rt, one);
                sw.Stop();
                (mode == 1 ? ons : offs).Add(sw.Elapsed.TotalMilliseconds);
            }
            Object.DestroyImmediate(one);
            rt.Release();
            Object.DestroyImmediate(rt);
            return ((float)Median(offs), (float)Median(ons));
        }

        static double Median(List<double> v)
        {
            v.Sort();
            return v.Count == 0 ? 0.0 : (v.Count & 1) == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        static void Drain(RenderTexture rt, Texture2D one)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = prev;
        }

        static int Even(int w) => (w & 1) == 1 ? w + 1 : w;

        static string Env(string name, string fallback)
        {
            string s = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        static int EnvInt(string name, int fallback) =>
            int.TryParse(System.Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;
    }
}
