using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.OnFoot;
using PSXRacing.LifeSim;
using Debug = UnityEngine.Debug;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CAR'S SHAPE ON A POLE (the replayfx round, 2026-11-26; the owner,
    /// over a night camcorder frame: "During replays I can see an illuminated
    /// silhouette of cars through objects"). CarDots kept every pixel where
    /// car paint had been DRAWN out of the SSAO, including the ones a pole or
    /// a leaf card was drawn over afterwards; <see cref="CarDots.Seen"/> keeps
    /// only the paint that is on screen.
    ///
    /// Stages a car behind a pole close to the lens and behind a cutout leaf
    /// card, by day and by night, wide and zoomed in (a camcorder's long lens),
    /// with the PLAYER's SSAO on (<see cref="CarDotsShots.PlayerAO"/>: the
    /// editor never runs it otherwise), and draws each frame TODAY
    /// (Seen off) and NEW (Seen on), through the PC picture, plus the kept
    /// pixels painted magenta (raw) for the count. Then the cost: eight cars in
    /// view, Seen off / on / off / on blocks of timed renders.
    ///
    /// Batch: -executeMethod PSXRacing.EditorTools.CarSeenShots.Run
    ///   PSX_CARSEEN_STAGE  the stage scene (default BlueRidge)
    /// Writes Screenshots/carseen/ (PNGs + carseen.txt).
    /// </summary>
    public static class CarSeenShots
    {
        static readonly StringBuilder log = new StringBuilder();
        static string outDir;
        static readonly int DebugId = Shader.PropertyToID("_PSXCarKeepDebug");

        public static void Run()
        {
            log.Clear();
            outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "carseen");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            System.Environment.SetEnvironmentVariable("PSX_PROFILE", "PC");
            bool seenWas = CarDots.Seen, onWas = CarDots.On;
            int wW = CarDotsShots.W, wH = CarDotsShots.H;
            CarDotsShots.W = 1920; CarDotsShots.H = 1080;
            try
            {
                CarDots.On = true; CarDots.Reset(); PSXTexDecode.StampLoaded();
                Stage("Assets/PSXRacing/Scenes/" + Env("PSX_CARSEEN_STAGE", "BlueRidge") + ".unity");
            }
            catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Debug.LogException(e); }
            finally
            {
                CarDotsShots.PlayerAO.Restore();
                Shader.SetGlobalFloat(DebugId, 0f);
                CarDots.Seen = seenWas; CarDots.On = onWas;
                CarDotsShots.W = wW; CarDotsShots.H = wH;
                log.AppendLine("CARSEEN DONE");
                File.WriteAllText(Path.Combine(outDir, "carseen.txt"), log.ToString());
            }
        }

        /// <summary>The showroom stage (the dots self-test's): the kept pixels TODAY and NEW for each car, saved and counted
        /// (NEW must keep every paint pixel TODAY keeps: nothing stands in front of a car there).</summary>
        public static void RunStudio()
        {
            log.Clear();
            outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "carseen");
            Directory.CreateDirectory(outDir);
            System.Environment.SetEnvironmentVariable("PSX_PROFILE", "PC");
            bool seenWas = CarDots.Seen;
            CarDots.On = true; CarDots.Reset(); PSXTexDecode.StampLoaded();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var (key, skin) in CarDotsShots.ParseCars(Env("PSX_CARSEEN_CARS", "civic_eg:red,fastback_96:white,gt_hardtop_73:silver")))
            {
                var def = CarModelLibrary.Load(key);
                var canvasGo = new GameObject("CS_Canvas");
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var panelGo = new GameObject("CS_Panel", typeof(RectTransform));
                panelGo.transform.SetParent(canvasGo.transform, false);
                var panel = (RectTransform)panelGo.transform;
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.sizeDelta = new Vector2(1280f, 600f);
                var host = new GameObject("CS_Viewer");
                var viewer = host.AddComponent<CarViewer>();
                viewer.Show(def, skin);
                viewer.AttachStudio(panel);
                viewer.ResetStudioAngle();
                viewer.TurnBy(40f - viewer.Yaw);
                viewer.Tick(0f, 0f);
                CarDotsShots.PlayerAO.Set(true);
                Shader.SetGlobalFloat(DebugId, 1f);
                CarDots.Seen = false; viewer.RenderNow();
                var a = CarDotsShots.Read(viewer.StudioTexture);
                CarDots.Seen = true; viewer.RenderNow();
                var b = CarDotsShots.Read(viewer.StudioTexture);
                Shader.SetGlobalFloat(DebugId, 0f);
                CarDotsShots.PlayerAO.Restore();
                var c = Compare(a, b, null, null, null);
                log.AppendLine($"studio {key}:{skin}: kept TODAY {c.today} NEW {c.now}; TODAY only {c.patch}, NEW only {c.newOnly}");
                File.WriteAllBytes(Path.Combine(outDir, $"studio_{key}_today_keep.png"), a.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(outDir, $"studio_{key}_new_keep.png"), b.EncodeToPNG());
                // the missed pixels white on dim magenta
                var pa = a.GetPixels32(); var pb = b.GetPixels32();
                for (int i = 0; i < pa.Length; i++)
                    pa[i] = Magenta(pa[i]) && !Magenta(pb[i]) ? new Color32(255, 255, 255, 255) : Magenta(pa[i]) ? new Color32(60, 0, 60, 255) : new Color32(0, 0, 0, 255);
                a.SetPixels32(pa); a.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"studio_{key}_missed.png"), a.EncodeToPNG());
                Object.DestroyImmediate(a); Object.DestroyImmediate(b);
                Object.DestroyImmediate(host); Object.DestroyImmediate(canvasGo);
            }
            CarDots.Seen = seenWas;
            File.WriteAllText(Path.Combine(outDir, "carseen_studio.txt"), log.ToString());
        }

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        // ------------------------------------------------------------------
        //  Materials from the scene: a pole's or a trunk's, a leaf card's
        // ------------------------------------------------------------------
        static Material FindMat(System.Func<Renderer, Material, bool> ok)
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                foreach (var m in r.sharedMaterials)
                    if (m != null && ok(r, m)) return m;
            return null;
        }

        public static Material PoleMat()
        {
            var m = FindMat((r, x) => x.shader.name.StartsWith("PSX/Lit") && (r.name.ToLowerInvariant().Contains("pole") || x.name.ToLowerInvariant().Contains("pole")));
            m ??= FindMat((r, x) => x.shader.name.StartsWith("PSX/Lit") && (x.name.ToLowerInvariant().Contains("bark") || x.name.ToLowerInvariant().Contains("trunk") || x.name.ToLowerInvariant().Contains("wood")));
            if (m != null) return m;
            var sh = Shader.Find("PSX/Lit");
            // none in the scene: a creosoted pole's vertical grain
            var tex = new Texture2D(64, 256, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[64 * 256];
            var rnd = new System.Random(11);
            var col = new float[64];
            for (int x = 0; x < 64; x++) col[x] = 0.75f + 0.25f * (float)rnd.NextDouble();
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 64; x++)
                {
                    float k = col[x] * (0.9f + 0.1f * (float)rnd.NextDouble());
                    px[y * 64 + x] = new Color32((byte)(120 * k), (byte)(88 * k), (byte)(62 * k), 255);
                }
            tex.SetPixels32(px); tex.Apply();
            m = new Material(sh) { name = "CarSeenPole", mainTexture = tex };
            PSXTexDecode.Stamp(m);
            return m;
        }

        public static Material LeafMat()
        {
            var m = FindMat((r, x) => x.HasProperty("_Cutoff") && x.GetFloat("_Cutoff") > 0.05f && x.mainTexture != null && x.renderQueue < 2500
                                       && !x.shader.name.Contains("Car"));
            if (m != null) return m;
            // none in the scene: a PSX/Lit cutout over a generated leaf mask
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[64 * 64];
            var rnd = new System.Random(7);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(40, 70, 30, (byte)(rnd.NextDouble() < 0.55 ? 255 : 0));
            tex.SetPixels32(px); tex.Apply();
            m = new Material(Shader.Find("PSX/Lit")) { name = "CarSeenLeaves", mainTexture = tex, renderQueue = 2450 };
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            return m;
        }

        // ------------------------------------------------------------------
        //  The staged shots
        // ------------------------------------------------------------------
        static void Stage(string scenePath)
        {
            if (!File.Exists(scenePath)) { log.AppendLine("no scene " + scenePath); return; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            var cam = output != null ? output.GetComponent<Camera>() : Camera.main;
            if (cam == null) { log.AppendLine("no camera"); return; }
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            var player = Object.FindFirstObjectByType<CarController>();
            var spot = new GameObject("CarSeenSpot").transform;
            Transform from = player != null ? player.transform : cam.transform;
            spot.SetPositionAndRotation(from.position, Quaternion.Euler(0f, from.eulerAngles.y, 0f));
            if (Physics.Raycast(spot.position + Vector3.up * 5f, Vector3.down, out var hit, 50f)) spot.position = hit.point;
            foreach (var cc in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                foreach (var r in cc.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var poleMat = PoleMat(); var leafMat = LeafMat();
            log.AppendLine($"scene {Path.GetFileNameWithoutExtension(scenePath)} camera {cam.name} spot {spot.position} pole {poleMat.name} ({poleMat.shader.name}) leaves {leafMat.name} ({leafMat.shader.name}, cutoff {leafMat.GetFloat("_Cutoff"):0.00})");

            var def = CarModelLibrary.Load("civic_eg");
            var shell = CarShell.Spawn(spot, def, 0, out _, solid: false);
            PSXTexDecode.StampTree(shell.gameObject);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
            foreach (var (hourName, hour) in new[] { ("day", TimeOfDay.Noon), ("night", TimeOfDay.Night) })
            {
                if (sun != null) TimeOfDay.Apply(hour, sun);
                var g = Object.FindFirstObjectByType<PSXGlobals>();
                if (g != null) g.Apply();
                // by night a roadside lamp over the car (a mountain stage has none; the owner's frame was under city lamps)
                var lamps = new List<int>();
                if (hour == TimeOfDay.Night)
                {
                    lamps.Add(StreetLights.Add(null, spot.position + spot.right * 3f + Vector3.up * 7f, 22f, StreetLights.Bulb, StreetLights.StreetIntensity, StreetLights.Kind.Point));
                    lamps.Add(StreetLights.Add(null, spot.position + spot.right * 9f + spot.forward * 6f + Vector3.up * 7f, 22f, StreetLights.Bulb, StreetLights.StreetIntensity, StreetLights.Kind.Point));
                }
                foreach (var (setup, distM, fov, poleAt, poleR) in new[] { ("wide", 10f, 50f, 1.0f, 0.12f), ("zoom", 45f, 10f, 2.2f, 0.13f) })
                {
                    Vector3 c = spot.position + Vector3.up * 0.6f;
                    Vector3 side = spot.right;
                    var eye = c + side * distM + Vector3.up * 1.0f;
                    var dir = (c - eye).normalized;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir));
                    cam.fieldOfView = fov;
                    // the pole over the car's front half, the leaves over its rear
                    var right = cam.transform.right;
                    float halfCarDeg = Mathf.Atan(1.0f / distM) * Mathf.Rad2Deg;
                    var poleDir = Quaternion.AngleAxis(-halfCarDeg, Vector3.up) * dir;
                    var leafDir = Quaternion.AngleAxis(halfCarDeg * 1.1f, Vector3.up) * dir;
                    var objs = Occluders(eye, poleDir, poleAt, poleR, leafDir, poleAt * 4.5f, poleMat, leafMat);
                    StreetLights.Push(eye, dir);
                    Shoot(cam, output, $"{hourName}_{setup}", objs);
                    foreach (var o in objs) Object.DestroyImmediate(o);
                }
                foreach (var h in lamps) StreetLights.Remove(h);
            }
            Perf(cam, spot, def);
            Object.DestroyImmediate(shell.gameObject);
        }

        /// <summary>A pole at <paramref name="poleAt"/> m along poleDir and a leaf card further along leafDir.</summary>
        public static List<GameObject> Occluders(Vector3 eye, Vector3 poleDir, float poleAt, float poleR, Vector3 leafDir, float leafAt, Material poleMat, Material leafMat)
        {
            var list = new List<GameObject>();
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "CarSeenPole";
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            var pp = eye + poleDir * poleAt;
            pole.transform.position = new Vector3(pp.x, eye.y, pp.z);
            pole.transform.localScale = new Vector3(poleR * 2f, 6f, poleR * 2f);
            // Drawn AFTER the car, as the city's poles are: they ride in a tile's batched mesh whose
            // bounds sort it behind the car, and a lone pole close to the lens would be drawn first
            // (front to back) and never show the bug. (Cutout leaves are always after: queue 2450.)
            pole.GetComponent<Renderer>().sharedMaterial = new Material(poleMat) { name = poleMat.name + " (after the cars)", renderQueue = 2001 };
            pole.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;   // (no shadow on the car: the footprint is what it covers)
            list.Add(pole);
            if (leafMat != null)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Quad);
                leaf.name = "CarSeenLeaves";
                Object.DestroyImmediate(leaf.GetComponent<Collider>());
                leaf.transform.position = eye + leafDir * leafAt;
                leaf.transform.rotation = Quaternion.LookRotation(leafDir);
                float s = leafAt * 0.35f;
                leaf.transform.localScale = new Vector3(s, s, 1f);
                leaf.GetComponent<Renderer>().sharedMaterial = leafMat;
                leaf.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                list.Add(leaf);
            }
            return list;
        }

        static void Shoot(Camera cam, PSXCameraOutput output, string name, List<GameObject> objs)
        {
            var v = Footprint(cam, output, objs);
            log.AppendLine($"  {name}: occluders cover {v.occluderPx} px, {v.carUnderPx} of them over the car; kept there TODAY {v.keptTodayOver} px, NEW {v.keptNewOver} px; kept on the open car TODAY {v.keptTodayOpen} NEW {v.keptNewOpen}");
            var shots = new Dictionary<string, Texture2D>();
            foreach (bool seen in new[] { false, true })
            {
                CarDots.Seen = seen;
                CarDotsShots.PlayerAO.Set(true);
                int f0 = CarDots.Frames, s0 = CarDots.SeenFrames;
                shots[seen ? "new" : "today"] = CarDotsShots.Render(cam, output, true);
                Shader.SetGlobalFloat(DebugId, 1f);
                shots[seen ? "newkeep" : "todaykeep"] = CarDotsShots.Render(cam, output, false);
                Shader.SetGlobalFloat(DebugId, 0f);
                CarDotsShots.PlayerAO.Restore();
                log.AppendLine($"  {name} {(seen ? "NEW  " : "TODAY")}: keep passes {CarDots.Frames - f0} frames, seen mark {CarDots.SeenFrames - s0} frames");
            }
            CarDotsShots.PlayerAO.Set(false);
            shots["noao"] = CarDotsShots.Render(cam, output, true);
            CarDotsShots.PlayerAO.Restore();
            var c = Compare(shots["todaykeep"], shots["newkeep"], shots["today"], shots["new"], shots["noao"]);
            log.AppendLine($"  {name}: kept px TODAY {c.today}  NEW {c.now}  | kept TODAY only (the patch) {c.patch} px  NEW only {c.newOnly} px" +
                           $"  | on the patch: luma TODAY {c.lumaToday:0.000}  NEW {c.lumaNew:0.000}  no-AO {c.lumaNoAO:0.000}");
            foreach (var kv in shots)
            {
                File.WriteAllBytes(Path.Combine(outDir, $"{name}_{kv.Key}.png"), kv.Value.EncodeToPNG());
                Object.DestroyImmediate(kv.Value);
            }
        }

        static int dumps;

        public struct Verdict { public int occluderPx, carUnderPx, keptTodayOver, keptNewOver, keptTodayOpen, keptNewOpen; }

        /// <summary>
        /// Where the occluders stand and what is kept there: the raw frame with
        /// and without them (the footprint = every pixel they change); the kept
        /// pixels (magenta, raw) of the car alone, and with them TODAY and NEW.
        /// Over the car's covered part NEW must keep nothing; on the car's open
        /// part NEW must keep what TODAY keeps.
        /// </summary>
        public static Verdict Footprint(Camera cam, PSXCameraOutput output, List<GameObject> objs)
        {
            bool seenWas = CarDots.Seen;
            var v = new Verdict();
            try
            {
                CarDotsShots.PlayerAO.Set(true);
                foreach (var o in objs) o.SetActive(false);
                var bare = CarDotsShots.Render(cam, output, false);
                Shader.SetGlobalFloat(DebugId, 1f);
                CarDots.Seen = false;   // (no occluders: every drawn paint pixel is the car on screen)
                var carKeep = CarDotsShots.Render(cam, output, false);
                foreach (var o in objs) o.SetActive(true);
                Shader.SetGlobalFloat(DebugId, 0f);
                // the footprint: the occluders drawn flat green (their own cutout and queue), nothing else changed
                var kept = new List<(Renderer, Material)>();
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                foreach (var o in objs)
                {
                    var r = o.GetComponent<Renderer>();
                    var m0 = r.sharedMaterial;
                    var g = new Material(unlit) { renderQueue = m0.renderQueue };
                    g.SetColor("_BaseColor", Color.green);
                    if (m0.HasProperty("_Cutoff") && m0.GetFloat("_Cutoff") > 0.01f)
                    {
                        g.SetTexture("_BaseMap", m0.mainTexture);
                        g.SetFloat("_AlphaClip", 1f); g.SetFloat("_Cutoff", m0.GetFloat("_Cutoff"));
                        g.EnableKeyword("_ALPHATEST_ON");
                    }
                    kept.Add((r, m0));
                    r.sharedMaterial = g;
                }
                var plain = CarDotsShots.Render(cam, output, false);
                foreach (var (r, m0) in kept) { var g = r.sharedMaterial; r.sharedMaterial = m0; Object.DestroyImmediate(g); }
                Shader.SetGlobalFloat(DebugId, 1f);
                CarDots.Seen = false;
                var keepToday = CarDotsShots.Render(cam, output, false);
                CarDots.Seen = true;
                var keepNew = CarDotsShots.Render(cam, output, false);
                Shader.SetGlobalFloat(DebugId, 0f);
                var dump = System.Environment.GetEnvironmentVariable("PSX_CARSEEN_DUMP");
                if (!string.IsNullOrEmpty(dump))
                {
                    Directory.CreateDirectory(dump);
                    string tag = "fp" + (dumps++);
                    File.WriteAllBytes(Path.Combine(dump, tag + "_bare.png"), bare.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dump, tag + "_plain.png"), plain.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dump, tag + "_carkeep.png"), carKeep.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dump, tag + "_keeptoday.png"), keepToday.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dump, tag + "_keepnew.png"), keepNew.EncodeToPNG());
                }
                var b = bare.GetPixels32(); var p = plain.GetPixels32(); var car = carKeep.GetPixels32();
                var kt = keepToday.GetPixels32(); var kn = keepNew.GetPixels32();
                for (int i = 0; i < b.Length; i++)
                {
                    bool occ = (p[i].g > 200 && p[i].r < 80 && p[i].b < 80) || System.Math.Abs(b[i].r - p[i].r) + System.Math.Abs(b[i].g - p[i].g) + System.Math.Abs(b[i].b - p[i].b) > 40;
                    bool carHere = Magenta(car[i]);
                    if (occ) v.occluderPx++;
                    if (occ && carHere)
                    {
                        v.carUnderPx++;
                        if (Magenta(kt[i])) v.keptTodayOver++;
                        if (Magenta(kn[i])) v.keptNewOver++;
                    }
                    else if (carHere)
                    {
                        if (Magenta(kt[i])) v.keptTodayOpen++;
                        if (Magenta(kn[i])) v.keptNewOpen++;
                    }
                }
                foreach (var t in new[] { bare, carKeep, plain, keepToday, keepNew }) Object.DestroyImmediate(t);
            }
            finally
            {
                Shader.SetGlobalFloat(DebugId, 0f);
                CarDotsShots.PlayerAO.Restore();
                foreach (var o in objs) o.SetActive(true);
                CarDots.Seen = seenWas;
            }
            return v;
        }

        public struct Counts { public int today, now, patch, newOnly; public float lumaToday, lumaNew, lumaNoAO; }

        static bool Magenta(Color32 p) => p.r > 200 && p.g < 60 && p.b > 200;
        static float Luma(Color32 p) => (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;

        /// <summary>Kept pixels today / new, the pixels kept only today (the car's shape on what covers it),
        /// and the picture's mean luma on those pixels today, new and with no SSAO at all.</summary>
        public static Counts Compare(Texture2D keepToday, Texture2D keepNew, Texture2D today, Texture2D now, Texture2D noao)
        {
            var a = keepToday.GetPixels32(); var b = keepNew.GetPixels32();
            var t = today?.GetPixels32(); var n = now?.GetPixels32(); var z = noao?.GetPixels32();
            var c = new Counts();
            double lt = 0, ln = 0, lz = 0;
            for (int i = 0; i < a.Length; i++)
            {
                bool ka = Magenta(a[i]), kb = Magenta(b[i]);
                if (ka) c.today++;
                if (kb) c.now++;
                if (kb && !ka) c.newOnly++;
                if (ka && !kb)
                {
                    c.patch++;
                    if (t != null) { lt += Luma(t[i]); ln += Luma(n[i]); lz += Luma(z[i]); }
                }
            }
            if (c.patch > 0) { c.lumaToday = (float)(lt / c.patch); c.lumaNew = (float)(ln / c.patch); c.lumaNoAO = (float)(lz / c.patch); }
            return c;
        }

        // ------------------------------------------------------------------
        //  The cost: eight cars in view
        // ------------------------------------------------------------------
        static void Perf(Camera cam, Transform spot, CarModelDef def)
        {
            var shells = new List<Transform>();
            var keys = new[] { "civic_eg", "fastback_96", "gt_liftback_88", "gt_hardtop_73", "gtr_99", "charger_69", "civic_eg", "fastback_96" };
            var holder = new GameObject("CarSeenPerf").transform;
            for (int i = 0; i < 8; i++)
            {
                var d = CarModelLibrary.Load(keys[i]) ?? def;
                var at = new GameObject("p" + i).transform;
                at.SetParent(holder);
                at.SetPositionAndRotation(spot.position + spot.forward * ((i % 4) * 6f - 9f) + spot.right * ((i / 4) * 4f - 2f), spot.rotation);
                var s = CarShell.Spawn(at, d, 0, out _, solid: false);
                if (s != null) { PSXTexDecode.StampTree(s.gameObject); shells.Add(s); }
            }
            var eye = spot.position + spot.right * 22f + Vector3.up * 4f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(spot.position - eye));
            cam.fieldOfView = 60f;
            int draws = 0;
            foreach (var s in shells)
                foreach (var r in s.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.shader != null && m.shader.name == "PSX/CarPaint" && m.GetFloat("_PSXHoleMode") < 0.5f) draws++;
            log.AppendLine($"PERF: {shells.Count} cars in view, PSX/CarPaint renderer-materials {draws} (= draws the seen mark adds, one each, no colour)");
            CarDotsShots.PlayerAO.Set(true);
            var rt = new RenderTexture(1920, 1080, 32, RenderTextureFormat.ARGB32);
            rt.Create();
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            // Frame by frame, OFF and ON alternating (drift and warm-up fall on both alike); medians. The control:
            // the OFF frames split in two interleaved halves, which differ only by the noise.
            var offs = new List<double>(); var ons = new List<double>();
            for (int k = 0; k < 10; k++) RenderPipeline.SubmitRenderRequest(cam, req);
            var sw = new Stopwatch();
            for (int k = 0; k < 1200; k++)
            {
                bool seen = (k & 1) == 1;
                CarDots.Seen = seen;
                sw.Restart();
                RenderPipeline.SubmitRenderRequest(cam, req);
                RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null;   // waits for the GPU
                double ms = sw.Elapsed.TotalMilliseconds;
                (seen ? ons : offs).Add(ms);
            }
            CarDotsShots.PlayerAO.Restore();
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
            double Med(IEnumerable<double> s) { var a = s.OrderBy(x => x).ToArray(); return a[a.Length / 2]; }
            double mOff = Med(offs), mOn = Med(ons);
            double cA = Med(offs.Where((x, i) => i % 2 == 0)), cB = Med(offs.Where((x, i) => i % 2 == 1));
            log.AppendLine($"PERF (1920x1080, SSAO on, one frame + GPU wait each, 600 OFF / 600 ON alternating): median OFF {mOff:0.000} ms, ON {mOn:0.000} ms, " +
                           $"ON - OFF {mOn - mOff:+0.000;-0.000} ms; same-state control (OFF halves) {cA - cB:+0.000;-0.000} ms; " +
                           $"means OFF {offs.Average():0.000} ON {ons.Average():0.000}");
            Object.DestroyImmediate(holder.gameObject);
        }
    }
}
