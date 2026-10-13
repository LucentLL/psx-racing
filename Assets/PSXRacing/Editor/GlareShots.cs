using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The sun-in-the-lens check (SunGlareFx, 2026-10-13, the GT look plan
    /// item 2), written as Screenshots\glare_&lt;venue&gt;_&lt;hour&gt;_&lt;shot&gt;.png:
    ///   off      no glare (the picture as it was);
    ///   on       the glare, eye adapted (steady state);
    ///   blocked  a wall between the lens and the sun: no glare at all;
    ///   flash    the first frame after the wall is gone: the eye not yet closed;
    ///   settled  two seconds of frames later: back to "on";
    ///   edge     the sun half out of the frame: about half of it.
    /// Each frame's eye pixel (seen, eye) goes to the log as [GlareShot].
    /// PSX_GLARE_VENUES=BlueRidge,CityCircuit picks the venues;
    /// PSX_GLARE_HOURS=afternoon,sunset the hours.
    /// Headless (with graphics): -executeMethod PSXRacing.EditorTools.GlareShots.Run
    /// </summary>
    public static class GlareShots
    {
        public static void Run()
        {
            string venues = Env("PSX_GLARE_VENUES", "BlueRidge");
            string hours = Env("PSX_GLARE_HOURS", "afternoon,sunset");
            foreach (var id in venues.Split(','))
            {
                var def = System.Array.Find(TrackCatalog.All, d => d.id == id.Trim());
                if (def == null) { Debug.LogWarning("[GlareShot] no venue " + id); continue; }
                if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Debug.LogWarning("[GlareShot] " + id + " did not open"); continue; }
                foreach (var hourName in hours.Split(','))
                {
                    int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(hourName.Trim(), System.StringComparison.OrdinalIgnoreCase));
                    if (hour < 0) { Debug.LogWarning("[GlareShot] no hour " + hourName); continue; }
                    Shoot(def, cam, player, hour);
                }
                SunGlareFx.PreviewSet(null, 0f, 0f, true);
                if (Env("PSX_GLARE_WET", "1") == "1") WetShots(def, cam, player);
                Debug.Log("[GlareShot] " + def.id + " done");
            }
        }

        static void Shoot(TrackCatalog.TrackDef def, Camera cam, GameObject player, int hour)
        {
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            TimeOfDay.Apply(hour, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            float strength = TimeOfDay.ShadowFor(hour, Weather.Clear);
            Vector3 toSun = TimeOfDay.SunSeen;
            Vector3 flat = new Vector3(toSun.x, 0f, toSun.z);
            if (flat.sqrMagnitude < 1e-4f) flat = player.transform.forward;
            flat.Normalize();
            float elev = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;

            // Behind the car, looking at the sun with it a dozen degrees above
            // the middle of the frame (the car below it).
            var t = player.transform;
            Vector3 eye = t.position + Vector3.up * 1.8f - flat * 6f;
            float pitch = Mathf.Max(elev - 12f, -8f);
            var rot = Quaternion.LookRotation(Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat);
            string tag = $"glare_{def.id}_{TimeOfDay.All[hour].name.ToLowerInvariant()}";
            Debug.Log($"[GlareShot] {tag} sun elevation {elev:F1} deg, strength {strength:F2}");

            SunGlareFx.PreviewSet(null, 0f, 0f, true);
            PSXScreenshotTool.ShotAs(cam, tag + "_off", eye, rot);

            Shot(cam, tag + "_on", eye, rot, strength, 0f, true);

            // A wall between the lens and the sun, drawn by the game's own
            // shader so it writes the emitter mask as every surface does.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "GlareShotWall";
            Object.DestroyImmediate(wall.GetComponent<Collider>());
            var lit = Shader.Find("PSX/Lit");
            if (lit != null) wall.GetComponent<Renderer>().sharedMaterial = new Material(lit);
            wall.transform.position = eye + toSun * 8f;
            wall.transform.rotation = Quaternion.LookRotation(toSun);
            wall.transform.localScale = new Vector3(3f, 3f, 0.3f);
            Shot(cam, tag + "_blocked", eye, rot, strength, 0f, true);
            Object.DestroyImmediate(wall.GetComponent<Renderer>().sharedMaterial);
            Object.DestroyImmediate(wall);

            // The wall gone: a tenth of a second later the sun is fully seen
            // and the eye is still opened-up for the dark, so the frame flashes...
            Frame(cam, eye, rot, strength, 1f / 30f);
            Frame(cam, eye, rot, strength, 1f / 30f);
            Shot(cam, tag + "_flash", eye, rot, strength, 1f / 30f, false);
            // ...and two seconds of frames later it has settled.
            for (int i = 0; i < 8; i++) Frame(cam, eye, rot, strength, 0.25f);
            Shot(cam, tag + "_settled", eye, rot, strength, 1f / 30f, false);

            // The sun at the frame's edge: turned away by half the width.
            float halfH = Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (16f / 9f)) * Mathf.Rad2Deg;
            var edge = Quaternion.AngleAxis(halfH, Vector3.up) * rot;
            Shot(cam, tag + "_edge", eye, edge, strength, 0f, true);
            SunGlareFx.PreviewSet(null, 0f, 0f, true);
        }

        /// <summary>
        /// The world in the water (PSX/Lit's wet road, 2026-10-13): a rainy
        /// afternoon down the road from behind the car, the road mirroring
        /// the sky lookup alone (wet_off) and CarReflections' panorama (wet_on).
        /// </summary>
        static void WetShots(TrackCatalog.TrackDef def, Camera cam, GameObject player)
        {
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            RaceHandoff.WeatherOverride = (int)Weather.Rain;
            TimeOfDay.Apply(TimeOfDay.Afternoon, sun);
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            var t = player.transform;
            var views = new (string name, Vector3 eye, Vector3 aim)[]
            {
                ("chase", t.position - t.forward * 6.5f + Vector3.up * 2.0f, t.position + t.forward * 25f),
                ("low", t.position + t.right * 2.5f + t.forward * 4f + Vector3.up * 0.8f, t.position + t.forward * 60f + Vector3.up * 2f),
            };
            foreach (var v in views)
            {
                var rot = Quaternion.LookRotation(v.aim - v.eye);
                CarReflections.Off();
                PSXScreenshotTool.ShotAs(cam, $"wet_{def.id}_{v.name}_off", v.eye, rot);
                if (!CarReflections.CaptureNow(t, cam)) { Debug.LogError("[GlareShot] EnvPano shader missing"); break; }
                PSXScreenshotTool.ShotAs(cam, $"wet_{def.id}_{v.name}_on", v.eye, rot);
            }
            CarReflections.Off();
            RaceHandoff.WeatherOverride = -1;
            TimeOfDay.Apply(TimeOfDay.Afternoon, sun);
            Debug.Log("[GlareShot] wet " + def.id + " done");
        }

        static void Shot(Camera cam, string name, Vector3 eye, Quaternion rot, float strength, float dt, bool reset)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            SunGlareFx.PreviewSet(cam, strength, dt, reset);
            PSXScreenshotTool.ShotAs(cam, name, eye, rot);
            LogEye(name);
        }

        /// <summary>A frame that is rendered and thrown away: the eye moves on.</summary>
        static void Frame(Camera cam, Vector3 eye, Quaternion rot, float strength, float dt)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
            SunGlareFx.PreviewSet(cam, strength, dt, false);
            var rt = new RenderTexture(854, 480, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static void LogEye(string name)
        {
            var e = PSXRacing.SpeedBlurFeature.GlareEyeForTools;
            if (e == null) { Debug.Log($"[GlareShot] {name}: no eye (pass did not run)"); return; }
            var prev = RenderTexture.active;
            RenderTexture.active = e;
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            Color32 c = t.GetPixel(0, 0);
            Object.DestroyImmediate(t);
            float seen = (c.r + c.g / 255f) / 255f, eyeV = (c.b + c.a / 255f) / 255f;
            Debug.Log($"[GlareShot] {name}: seen {seen:F3} eye {eyeV:F3} sun {SunGlareFx.Sun}");
        }

        static string Env(string name, string fallback)
        {
            string s = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }
    }
}
