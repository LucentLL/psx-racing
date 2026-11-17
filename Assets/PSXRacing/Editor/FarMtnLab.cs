using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FAR MOUNTAIN ROUND's lab (owner 2026-11-17: "I like the rockface
    /// and distant tree textures on distant mountains. This should be a
    /// template for distant mountains on other maps" - PC49, Bat Cave, fall).
    ///
    ///   Build   PSX_FM_BUILD="LakeLure,BlueRidge": each venue's scene as the
    ///           PC scene pass builds it (BuildOneForLab, PSX_PC_CONTENT=1);
    ///           PSX_FM_AUDIT="..." adds the plant-through-solids and lane
    ///           obstacle audits. Writes Screenshots/fm_build_&lt;tag&gt;.txt.
    ///   Shots   PSX_FM_POSES = a JSON file of poses per venue (a road station
    ///           and a turn off the road's heading, or an eye and a look):
    ///           real editor frames through the game's own dither, the driver's
    ///           eye, per dress in PSX_FM_DRESSES ("Fall,Summer,Winter") at
    ///           PSX_FM_HOUR. PSX_FM_PARTS=1 adds, per pose, the frame without
    ///           the forest, without the Lake Lure granite / the template's
    ///           rock, and without the far canopy (what draws what).
    ///           PSX_FM_TITLE=1 adds the title postcard (LinnCoveShots, sets
    ///           "title"). Writes Screenshots/fm_&lt;tag&gt;/*.png + fm_&lt;tag&gt;.txt.
    ///   Perf    the template's runtime switch on and off at each pose,
    ///           rendered 24 times a side, ABAB (editor timing).
    /// Compiles against the base code too (the template's switch is found by
    /// reflection), so the base export carries it unchanged.
    /// </summary>
    public static class FarMtnLab
    {
        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }
        static string Root => Path.GetDirectoryName(Application.dataPath);
        static string Shots => Path.Combine(Root, "Screenshots");
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        // ------------------------------------------------------------ build
        public static void Build()
        {
            int code = BuildCore();
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>Build, then the shots of the venues just built (one editor, one lock).</summary>
        public static void BuildAndCapture()
        {
            int code = BuildCore();
            if (Env("PSX_FM_VENUES", "").Length == 0) System.Environment.SetEnvironmentVariable("PSX_FM_VENUES", Env("PSX_FM_BUILD", ""));
            code |= CaptureCore();
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static int BuildCore()
        {
            Directory.CreateDirectory(Shots);
            string tag = Env("PSX_FM_TAG", "x");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"farmtn build {tag}: PSX_FARMTN_TEMPLATE={Env("PSX_FARMTN_TEMPLATE", "-")} PSX_PC_CONTENT={Env("PSX_PC_CONTENT", "-")} {System.DateTime.Now:s}");
            string outPath = Path.Combine(Shots, "fm_build_" + tag + ".txt");
            var audit = new HashSet<string>(Env("PSX_FM_AUDIT", "").Split(','));
            int code = 0;
            try
            {
                foreach (var raw in Env("PSX_FM_BUILD", "LakeLure").Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    TrackCatalog.TrackDef def = null;
                    foreach (var d in TrackCatalog.Scened) if (d.id == id) def = d;
                    if (def == null) { sb.AppendLine("no venue " + id); code = 1; continue; }
                    var t0 = System.DateTime.Now;
                    string path = PSXRacingBuilder.BuildOneForLab(def);
                    string blog = Path.Combine(Root, "PSXRacing_lab_build_log.txt");
                    if (File.Exists(blog)) File.Copy(blog, Path.Combine(Shots, "fm_buildlog_" + tag + "_" + id + ".txt"), true);
                    sb.AppendLine($"=== {id} -> {path} in {(System.DateTime.Now - t0).TotalMinutes:0.0} min");
                    if (audit.Contains(id))
                        sb.AppendLine(PlantSolidAudit.AuditForLab(def, path) + "\n" + TrackObstacleAudit.AuditForLab(def, path));
                    File.WriteAllText(outPath, sb.ToString());
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); code = 1; }
            sb.AppendLine("DONE");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[FarMtnLab] " + sb);
            return code;
        }

        // ------------------------------------------------------------ poses
        [System.Serializable] public class Pose { public string name; public float m = -1f, yaw, pitch = 2f, lane = 1f, height = 1.15f, fov; public float[] eye, look; }
        [System.Serializable] public class VenuePoses { public string id; public Pose[] poses; }
        [System.Serializable] public class PoseFile { public VenuePoses[] venues; }

        class Scene
        {
            public Camera cam; public TrackPath path; public Light sun; public PSXGlobals globals;
            public SeasonDress dressing; public MeshForest forest; public StageCulling sc;
        }

        static Scene Open(string id)
        {
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { log.AppendLine("no scene " + id); return null; }
            var s = new Scene
            {
                cam = cam, path = Object.FindAnyObjectByType<TrackPath>(), sun = GameObject.Find("Sun")?.GetComponent<Light>(),
                globals = Object.FindAnyObjectByType<PSXGlobals>(), dressing = Object.FindAnyObjectByType<SeasonDress>(),
                forest = Object.FindAnyObjectByType<MeshForest>(), sc = cam.GetComponent<StageCulling>(),
            };
            if (s.path == null || s.globals == null || s.sun == null) { log.AppendLine("scene parts missing in " + id); return null; }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c.isRootCanvas && c.gameObject.name != "DisplayCanvas" && c.enabled) c.enabled = false;
            // the player's car out of the frame (the eye is the driver's, not the chase's)
            var car = GameObject.Find("RX-7 Player");
            if (car != null) car.SetActive(false);
            ApplySwitches();
            if (s.sc != null) s.sc.Configure(cam);
            log.AppendLine($"== {id}: far plane {cam.farClipPlane:0} m, horizon {TimeOfDay.HorizonM:0} m, fov {cam.fieldOfView:0.0}, " +
                           $"far canopy {(LookChoices.FarCanopy ? "on" : "off")}, template {TemplateState()}");
            return s;
        }

        /// <summary>The runtime switches as the player reads them at load (the
        /// template's by reflection: the base code has none).</summary>
        static void ApplySwitches()
        {
            LandmarkRocks.ApplyAll();
            FarCanopySwitch.ApplyAll();
            var t = System.Type.GetType("PSXRacing.FarMtnSwitch, Assembly-CSharp");
            t?.GetMethod("ApplyAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
            Physics.SyncTransforms();
        }

        static string TemplateState()
        {
            var t = System.Type.GetType("PSXRacing.LookChoices, Assembly-CSharp");
            var p = t?.GetProperty("FarMtnTemplate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return p == null ? "(no template in this code)" : ((bool)p.GetValue(null) ? "on" : "off");
        }

        static int DressOf(string name)
        {
            switch (name.Trim().ToLower())
            {
                case "winter": return (int)Season.Winter;
                case "spring": return (int)Season.Spring;
                case "summer": return (int)Season.Summer;
                case "snow": return Seasons.DressSnow;
                default: return (int)Season.Fall;
            }
        }

        static int DayFor(int dress)
        {
            if (dress == (int)Season.Fall || dress == Seasons.DressSnow) return 0;
            int first = -1, last = -1;
            for (int d = 1; d < 400; d++)
            {
                if ((int)Seasons.Of(d) == dress) { if (first < 0) first = d; last = d; }
                else if (first >= 0) break;
            }
            return first < 0 ? 0 : (first + last) / 2;
        }

        static void Condition(Scene s, int dress, string hourName, Vector3 eye)
        {
            RaceHandoff.CalendarDay = DayFor(dress);
            if (s.dressing != null)
            {
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(s.dressing, null);
                s.dressing.Apply(dress);
            }
            RaceHandoff.WeatherOverride = -1;
            Seasons.Forced = null; Seasons.Invalidate();
            var w = Seasons.Now;
            w.fog = false; w.precip = PrecipKind.None; w.precipRate = 0f;
            w.sky = SkyKind.Clear;
            Seasons.Forced = w;
            int hour = 0;
            for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == hourName.ToLower()) hour = h;
            if (s.sc != null) s.sc.Configure(s.cam);
            ApplySwitches();
            TimeOfDay.Apply(hour, s.sun);
            s.globals.Apply();
            DynamicSky.PrimeClouds(eye);
        }

        static void Grab(Scene s, string file, Vector3 eye, Quaternion rot, int h, bool forest = true, int cullOff = -1)
        {
            s.cam.transform.SetPositionAndRotation(eye, rot);
            if (s.sc != null) s.sc.Configure(s.cam);
            int mask = s.cam.cullingMask;
            if (cullOff >= 0) s.cam.cullingMask = mask & ~(1 << cullOff);
            SunShadows.RenderFor(s.cam);
            if (s.forest != null && forest) s.forest.DrawNow(s.cam);
            int w = Mathf.RoundToInt(h * 16f / 9f) & ~1;
            var rt = new RenderTexture(w, h, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            PSXTexDecode.StampAll();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(s.cam, req)) { log.AppendLine("render request unsupported"); s.cam.cullingMask = mask; return; }
            RenderPipeline.SubmitRenderRequest(s.cam, req);
            s.cam.cullingMask = mask;
            var shown = PSXScreenshotTool.Dithered(rt);
            var prev = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release(); Object.DestroyImmediate(rt);
        }

        static float GroundY(Vector3 at, float fallback) =>
            Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out var hit, 900f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;

        /// <summary>A pose's eye and turn: the driver's eye in the right-hand
        /// lane at station m, looking down the road turned by yaw and raised
        /// by pitch; or the file's own eye and look.</summary>
        static void PoseOf(Scene s, Pose p, out Vector3 eye, out Quaternion rot)
        {
            if (p.eye != null && p.eye.Length == 3 && p.look != null && p.look.Length == 3)
            {
                eye = new Vector3(p.eye[0], p.eye[1], p.eye[2]);
                var look = new Vector3(p.look[0], p.look[1], p.look[2]);
                rot = Quaternion.LookRotation(look - eye, Vector3.up);
                return;
            }
            var path = s.path;
            int i = Mathf.Clamp(Mathf.RoundToInt(p.m / TrackCatalog.Spacing), 1, path.Count - 2);
            Vector3 at = path.GetPoint(i), f = path.GetTangent(i); f.y = 0f; f.Normalize();
            var right = Vector3.Cross(Vector3.up, f);
            eye = at + right * (path.roadWidth * 0.25f * p.lane) + Vector3.up * (PSXRacingBuilder.RoadLift + p.height);
            rot = Quaternion.Euler(-p.pitch, Quaternion.LookRotation(f).eulerAngles.y + p.yaw, 0f);
        }

        public static void Capture()
        {
            int code = CaptureCore();
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static int CaptureCore()
        {
            Directory.CreateDirectory(Shots);
            string tag = Env("PSX_FM_TAG", "x");
            string dir = Path.Combine(Shots, "fm_" + tag);
            Directory.CreateDirectory(dir);
            if (Env("PSX_FM_KEEP", "0") != "1") foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            log.Clear();
            log.AppendLine($"farmtn shots {tag}: PSX_FARMTN_TEMPLATE={Env("PSX_FARMTN_TEMPLATE", "-")} PSX_FARCANOPY={Env("PSX_FARCANOPY", "-")} PC {PlatformProfile.PC} {System.DateTime.Now:s}");
            int code = 0;
            try
            {
                var file = JsonUtility.FromJson<PoseFile>(File.ReadAllText(Env("PSX_FM_POSES", "")));
                var dresses = Env("PSX_FM_DRESSES", "Fall,Summer").Split(',');
                string hour = Env("PSX_FM_HOUR", "NOON");
                int h = int.Parse(Env("PSX_FM_H", "720"), CultureInfo.InvariantCulture);
                bool parts = Env("PSX_FM_PARTS", "0") == "1";
                var only = Env("PSX_FM_VENUES", "");
                var want = only.Length > 0 ? new HashSet<string>(only.Split(',')) : null;
                foreach (var v in file.venues)
                {
                    if (want != null && !want.Contains(v.id)) continue;
                    var s = Open(v.id);
                    if (s == null) { code = 1; continue; }
                    float fov0 = s.cam.fieldOfView;
                    foreach (var p in v.poses)
                    {
                        PoseOf(s, p, out var eye, out var rot);
                        s.cam.fieldOfView = p.fov > 0f ? p.fov : fov0;
                        log.AppendLine($"  {v.id} {p.name}: eye ({eye.x:0.0},{eye.y:0.0},{eye.z:0.0}) yaw {rot.eulerAngles.y:0.0} pitch {-Mathf.DeltaAngle(0f, rot.eulerAngles.x):0.0} fov {s.cam.fieldOfView:0.0}" +
                                       (p.m >= 0f ? $" station {p.m:0}" : ""));
                        foreach (var dn in dresses)
                        {
                            int dress = DressOf(dn);
                            Condition(s, dress, hour, eye);
                            string stem = Path.Combine(dir, $"{v.id}_{p.name}_{dn.Trim().ToLower()}");
                            Grab(s, stem + ".png", eye, rot, h);
                            if (!parts) continue;
                            // what draws what: no forest (mesh forest and every card on the Foliage layer)
                            if (s.forest != null) s.forest.enabled = false;
                            Grab(s, stem + "_noforest.png", eye, rot, h, false, 10);
                            if (s.forest != null) s.forest.enabled = true;
                            // no granite (the Lake Lure world's, and the template's rock)
                            var hidden = new List<GameObject>();
                            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                                if (t.name.StartsWith("Granite") || t.name.StartsWith("FarRock")) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
                            Grab(s, stem + "_norock.png", eye, rot, h);
                            foreach (var g in hidden) g.SetActive(true);
                            // no far canopy (the land as it was before PSX_FARCANOPY)
                            LookChoices.FarCanopyForce = false;
                            if (s.sc != null) s.sc.Configure(s.cam);
                            FarCanopySwitch.ApplyAll();
                            Grab(s, stem + "_nocanopy.png", eye, rot, h);
                            LookChoices.FarCanopyForce = null;
                            if (s.sc != null) s.sc.Configure(s.cam);
                            FarCanopySwitch.ApplyAll();
                            log.AppendLine($"    parts: {hidden.Count} granite / rock roots hidden for the norock frame");
                        }
                    }
                    s.cam.fieldOfView = fov0;
                }
            }
            catch (System.Exception e) { log.AppendLine("THREW " + e); code = 1; }
            finally
            {
                LookChoices.FarCanopyForce = null;
                Seasons.Forced = null; Seasons.Invalidate();
            }
            if (Env("PSX_FM_TITLE", "0") == "1")
            {
                System.Environment.SetEnvironmentVariable("PSX_LC_SETS", "title");
                System.Environment.SetEnvironmentVariable("PSX_LC_TAG", "fm" + tag);
                try { LinnCoveShots.Capture(); log.AppendLine("title postcard: LinnCoveShots lc_fm" + tag + "_title_*"); }
                catch (System.Exception e) { log.AppendLine("title THREW " + e); code = 1; }
            }
            log.AppendLine("DONE");
            File.WriteAllText(Path.Combine(Shots, "fm_" + tag + ".txt"), log.ToString());
            Debug.Log("[FarMtnLab] " + log);
            return code;
        }
    }
}
