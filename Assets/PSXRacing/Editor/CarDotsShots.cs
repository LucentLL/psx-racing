using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DOTS ON THE PAINT IN SHADE (the cardots round, 2026-11-23; the owner:
    /// "Dots in shade...I see them when selecting a car to start the game. In
    /// menus. When parked in house driveway. Its been an issue for a long time").
    ///
    /// Shoots a parked car at the owner's 3832x2054 through the PC picture
    /// (PSX/Blit at 8 bits + FXAA + grade + halation, as PSXCameraOutput does
    /// in the player) in the garage, on a stage and on the showroom stage (the
    /// car viewer's studio render, which is what the NEW GAME pick, the car
    /// page, MY CARS and the dealers show), once per VARIANT - one switch at a
    /// time - and measures the paint's local contrast on the shaded side: the
    /// paint mask is PSX/CarPaint's own debug 12 (paint, not glass, not a
    /// wheel), the smooth shading is a 9x9 box blur, and a DOT is a paint pixel
    /// more than 0.035 (display) darker than its blur.
    ///
    /// THE PLAYER'S AO IN THE EDITOR: PlatformProfile.ApplyAO turns URP's SSAO
    /// after-opaque in the PLAYER only (the renderer asset is a file in the
    /// editor). <see cref="PlayerAO"/> does the same in memory for one render
    /// and puts every field back (nothing is marked dirty, nothing saved).
    ///
    /// Batch: -executeMethod PSXRacing.EditorTools.CarDotsShots.Run
    ///   PSX_CARDOTS_PLACES  garage,studio,stage (default all three)
    ///   PSX_CARDOTS_CARS    key:skin,... (default civic_eg red, gtr_99, a white, a black)
    ///   PSX_CARDOTS_VARIANTS names from <see cref="Variants"/> (default base,ao)
    ///   PSX_CARDOTS_STAGE   the stage scene (default BlueRidge)
    /// Writes Screenshots/cardots/ (crops + cardots.txt).
    /// </summary>
    public static class CarDotsShots
    {
        public static int W = 3832, H = 2054;
        static string outDir;
        static readonly StringBuilder log = new StringBuilder();

        // ------------------------------------------------------------------
        //  The player's SSAO, in memory
        // ------------------------------------------------------------------
        public static class PlayerAO
        {
            class Kept { public ScriptableRendererFeature f; public FieldInfo sf; public object settingsCopy; public bool active; }
            static readonly List<Kept> kept = new List<Kept>();
            const BindingFlags IF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

            static IEnumerable<ScriptableRendererFeature> Features()
            {
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var list = urp == null ? null : typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", IF)?.GetValue(urp) as ScriptableRendererData[];
                if (list == null) yield break;
                foreach (var d in list)
                    if (d != null)
                        foreach (var f in d.rendererFeatures)
                            if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") yield return f;
            }

            static object Clone(object s)
            {
                if (s == null) return null;
                var t = s.GetType();
                if (t.IsValueType) return s;
                var c = System.Activator.CreateInstance(t, true);
                foreach (var fi in t.GetFields(IF)) fi.SetValue(c, fi.GetValue(s));
                return c;
            }

            /// <summary>On: the player's settings (PlatformProfile.ApplyAO) plus any
            /// overrides; off (on=false): the feature switched off.</summary>
            public static string Set(bool on, System.Action<System.Action<string, object>> extra = null)
            {
                Restore();
                var sb = new StringBuilder();
                foreach (var f in Features())
                {
                    var sf = f.GetType().GetField("m_Settings", IF);
                    var s = sf?.GetValue(f);
                    if (s == null) continue;
                    kept.Add(new Kept { f = f, sf = sf, settingsCopy = Clone(s), active = f.isActive });
                    var t = s.GetType();
                    void Put(string name, object v) { var fi = t.GetField(name, IF); if (fi != null) fi.SetValue(s, fi.FieldType.IsEnum ? System.Enum.ToObject(fi.FieldType, v) : v); }
                    if (on)
                    {
                        Put("AfterOpaque", true);
                        Put("Source", 0);
                        Put("Intensity", PlatformProfile.AOIntensity);
                        Put("Radius", PlatformProfile.AORadius);
                        Put("Downsample", false);
                        extra?.Invoke(Put);
                    }
                    sf.SetValue(f, s);
                    f.SetActive(on);
                    foreach (var fi in t.GetFields(IF)) sb.Append(fi.Name).Append('=').Append(fi.GetValue(s)).Append(' ');
                }
                return sb.ToString();
            }

            public static void Restore()
            {
                foreach (var k in kept)
                {
                    if (k.settingsCopy != null) k.sf.SetValue(k.f, k.settingsCopy);
                    k.f.SetActive(k.active);
                }
                kept.Clear();
            }
        }

        // ------------------------------------------------------------------
        //  Variants: one switch at a time
        // ------------------------------------------------------------------
        class Variant { public string name; public System.Action on, off; }

        static Dictionary<string, Variant> Variants()
        {
            var d = new Dictionary<string, Variant>();
            void Add(string n, System.Action on, System.Action off) => d[n] = new Variant { name = n, on = on, off = off };
            // base: the editor as the shot tools have always drawn it (no AO); ao: the PLAYER's picture at 28480caf
            // (the SSAO after the opaques, no fix); fix: the player's picture with this round's fix (PSX_CARDOTS)
            Add("base", () => { Fix(false); PlayerAO.Set(false); }, PlayerAO.Restore);
            Add("ao", () => { Fix(false); log.AppendLine("  AO " + PlayerAO.Set(true)); }, PlayerAO.Restore);
            Add("fix", () => { Fix(true); PlayerAO.Set(true); }, () => { PlayerAO.Restore(); Fix(false); });
            Add("fix_noao", () => { Fix(true); PlayerAO.Set(false); }, () => { PlayerAO.Restore(); Fix(false); });
            Add("ao_hiq", () => PlayerAO.Set(true, p => { p("Samples", 0); p("BlurQuality", 0); }), PlayerAO.Restore);
            Add("ao_normals", () => PlayerAO.Set(true, p => { p("Source", 1); p("NormalSamples", 1); }), PlayerAO.Restore);
            Add("ao_inter", () => PlayerAO.Set(true, p => { p("AOMethod", 1); }), PlayerAO.Restore);
            Add("ao_lowblur", () => PlayerAO.Set(true, p => { p("BlurQuality", 2); }), PlayerAO.Restore);
            // the paint's own terms, with the player's AO on (debug views are measured as pictures too)
            Add("ao_noenv", () => { PlayerAO.Set(true); Shader.SetGlobalFloat("_PSXEnvOn", 0f); }, () => { PlayerAO.Restore(); });
            Add("ao_dbg_lit", () => { PlayerAO.Set(true); Shader.SetGlobalFloat("_PSXPaintDebug", 8f); }, () => { PlayerAO.Restore(); Shader.SetGlobalFloat("_PSXPaintDebug", 0f); });
            Add("dbg_lit", () => { PlayerAO.Set(false); Shader.SetGlobalFloat("_PSXPaintDebug", 8f); }, () => { PlayerAO.Restore(); Shader.SetGlobalFloat("_PSXPaintDebug", 0f); });
            Add("dbg_refl", () => { PlayerAO.Set(false); Shader.SetGlobalFloat("_PSXPaintDebug", 4f); }, () => { PlayerAO.Restore(); Shader.SetGlobalFloat("_PSXPaintDebug", 0f); });
            Add("dbg_tex", () => { PlayerAO.Set(false); Shader.SetGlobalFloat("_PSXPaintDebug", 5f); }, () => { PlayerAO.Restore(); Shader.SetGlobalFloat("_PSXPaintDebug", 0f); });
            Add("nopost", () => { PlayerAO.Set(false); rawOnly = true; }, () => { PlayerAO.Restore(); rawOnly = false; });
            Add("ao_nopost", () => { PlayerAO.Set(true); rawOnly = true; }, () => { PlayerAO.Restore(); rawOnly = false; });
            return d;
        }
        static bool rawOnly;

        /// <summary>The fix on or off for one variant: the switch, and every loaded material re-stamped.</summary>
        static void Fix(bool on)
        {
            CarDots.On = on;
            CarDots.Reset();
            PSXTexDecode.StampLoaded();
        }

        // ------------------------------------------------------------------
        //  Entry
        // ------------------------------------------------------------------
        public static void Run()
        {
            log.Clear();
            outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "cardots");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            System.Environment.SetEnvironmentVariable("PSX_PROFILE", "PC");
            string places = Env("PSX_CARDOTS_PLACES", "garage,studio,stage");
            bool fixWas = CarDots.On;
            try
            {
                Fix(false);
                foreach (var p in places.Split(','))
                {
                    var place = p.Trim();
                    try
                    {
                        if (place == "garage") World("Assets/PSXRacing/Scenes/Garage.unity", "garage");
                        else if (place == "stage") World("Assets/PSXRacing/Scenes/" + Env("PSX_CARDOTS_STAGE", "BlueRidge") + ".unity", "stage");
                        else if (place == "studio") Studio();
                    }
                    catch (System.Exception e) { log.AppendLine("EXCEPTION " + place + " " + e); Debug.LogException(e); }
                }
            }
            finally
            {
                PlayerAO.Restore();
                Fix(fixWas);
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                log.AppendLine("CARDOTS DONE");
                File.WriteAllText(Path.Combine(outDir, "cardots.txt"), log.ToString());
            }
        }

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        /// <summary>The default set: the owner's red hatchback, a white, a black and a silver metallic
        /// (the 11-11 / 11-16 shells), and two old low-poly sheets.</summary>
        public const string DefaultCars = "civic_eg:red,fastback_96:white,gt_liftback_88:black,gt_hardtop_73:silver,gtr_99:0,charger_69:silver";

        static List<(string key, int skin)> Cars() => ParseCars(Env("PSX_CARDOTS_CARS", DefaultCars));

        /// <summary>key:skin, the skin an index or a part of its sheet's name.</summary>
        public static List<(string key, int skin)> ParseCars(string spec)
        {
            var list = new List<(string, int)>();
            foreach (var s in spec.Split(','))
            {
                var a = s.Trim().Split(':');
                if (a.Length == 0 || a[0].Length == 0) continue;
                int skin = 0;
                if (a.Length > 1 && !int.TryParse(a[1], out skin))
                {
                    skin = -1;
                    var def = CarModelLibrary.Load(a[0]);
                    if (def != null && def.skinNames != null)
                        for (int i = 0; i < def.skinNames.Length; i++)
                            if (def.skinNames[i] != null && def.skinNames[i].ToLowerInvariant().Contains(a[1].ToLowerInvariant())) { skin = i; break; }
                    if (skin < 0) { log.AppendLine($"  {a[0]}: no skin like {a[1]} ({(def != null && def.skinNames != null ? string.Join(",", def.skinNames) : "-")})"); continue; }
                }
                list.Add((a[0], skin));
            }
            return list;
        }

        static List<Variant> Chosen()
        {
            var all = Variants();
            return Env("PSX_CARDOTS_VARIANTS", "base,ao,fix").Split(',').Select(s => s.Trim()).Where(all.ContainsKey).Select(s => all[s]).ToList();
        }

        // ------------------------------------------------------------------
        //  A parked car in a scene (the garage bay; a stage's start)
        // ------------------------------------------------------------------
        static void World(string scenePath, string tag)
        {
            if (!File.Exists(scenePath)) { log.AppendLine("no scene " + scenePath); return; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var output = Object.FindFirstObjectByType<PSXCameraOutput>();
            var cam = output != null ? output.GetComponent<Camera>() : Camera.main;
            if (cam == null) cam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.targetTexture == null);
            if (cam == null) { log.AppendLine(tag + ": no camera"); return; }
            Light sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            Transform spot;
            var gw = Object.FindFirstObjectByType<GarageWorld>();
            if (gw != null && gw.bays != null && gw.bays.Length > 0) spot = gw.bays[0];
            else
            {
                var sp = new GameObject("CarDotsSpot").transform;
                var start = GameObject.Find("StartLine") ?? GameObject.Find("Start") ?? GameObject.Find("SpawnPoint");
                var player = Object.FindFirstObjectByType<CarController>();
                Transform from = player != null ? player.transform : start != null ? start.transform : cam.transform;
                sp.SetPositionAndRotation(from.position, Quaternion.Euler(0f, from.eulerAngles.y, 0f));
                if (Physics.Raycast(sp.position + Vector3.up * 5f, Vector3.down, out var hit, 50f)) sp.position = hit.point;
                // every car the scene stands up (the player's, a rival's) out of the frame: only ours is drawn
                foreach (var cc in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                    foreach (var r in cc.GetComponentsInChildren<Renderer>()) r.enabled = false;
                spot = sp;
            }
            log.AppendLine($"{tag}: scene {Path.GetFileNameWithoutExtension(scenePath)} camera {cam.name} sun {(sun != null ? sun.name : "none")} spot {spot.position}");
            var camHome = cam.transform.position;
            cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000f;
            int hour = TimeOfDay.Noon;
            if (int.TryParse(Env("PSX_CARDOTS_HOUR", ""), out int hh)) hour = hh;
            if (sun != null && tag != "garage") TimeOfDay.Apply(hour, sun);
            var g = Object.FindFirstObjectByType<PSXGlobals>();
            if (g != null) g.Apply();
            if (sun != null) log.AppendLine($"  sun dir {sun.transform.forward} amb {Shader.GetGlobalColor("_PSXAmbient")} sky {Shader.GetGlobalColor("_PSXSkyAmbient")}");

            foreach (var (key, skin) in Cars())
            {
                var def = CarModelLibrary.Load(key);
                if (def == null || skin >= def.SkinCount) { log.AppendLine($"  {key}:{skin} missing"); continue; }
                var shell = CarShell.Spawn(spot, def, skin, out _, solid: false);
                if (shell == null) { log.AppendLine($"  {key} spawn failed"); continue; }
                PSXTexDecode.StampTree(shell.gameObject);
                var body = shell.Find("Body")?.GetComponent<Renderer>();
                // the side AWAY from the sun (the shaded flank), rear three-quarter, 4.2 m
                Vector3 c = spot.position, f = spot.forward, r = spot.right;
                float side = sun != null && Vector3.Dot(-sun.transform.forward, r) > 0f ? -1f : 1f;
                // indoors: from the room's side of the bay (toward where the scene's camera stood)
                Vector3 room = camHome - c; room.y = 0f;
                bool indoor = tag == "garage" && room.sqrMagnitude > 1f;
                foreach (var (vn, d0, h) in new[] { ("q42", 4.2f, 1.35f), ("q8", 8f, 1.6f) })
                {
                    float d = indoor ? Mathf.Min(d0, Mathf.Max(3f, room.magnitude * 0.9f)) : d0;
                    var dir = indoor ? (room.normalized + f * 0.25f).normalized : (-f * 0.55f + r * 0.83f * side).normalized;
                    var eye = c + dir * d + Vector3.up * h;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c + Vector3.up * 0.6f - eye));
                    StreetLights.Push(eye, cam.transform.forward);
                    Shoot(cam, output, body, $"{tag}_{key}_{skin}_{vn}");
                }
                Object.DestroyImmediate(shell.gameObject);
            }
        }

        // ------------------------------------------------------------------
        //  The showroom stage (CarViewer's studio render)
        // ------------------------------------------------------------------
        static void Studio()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvasGo = new GameObject("CD_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var panelGo = new GameObject("CD_Panel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1920f, 900f);
            var host = new GameObject("CD_Viewer");
            var viewer = host.AddComponent<CarViewer>();
            foreach (var (key, skin) in Cars())
            {
                var def = CarModelLibrary.Load(key);
                if (def == null || skin >= def.SkinCount) { log.AppendLine($"  studio {key}:{skin} missing"); continue; }
                viewer.Show(def, skin);
                viewer.AttachStudio(panel);
                viewer.ResetStudioAngle();
                foreach (float yaw in new[] { 215f, 40f })
                {
                    viewer.TurnBy(yaw - viewer.Yaw);
                    viewer.Tick(0f, 0f);
                    PlayerAO.Set(false);
                    Shader.SetGlobalFloat("_PSXPaintDebug", 12f);
                    viewer.RenderNow();
                    var mask = Read(viewer.StudioTexture);
                    Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                    PlayerAO.Restore();
                    Texture2D refTex = null;
                    foreach (var v in Chosen())
                    {
                        v.on();
                        viewer.RenderNow();
                        var tex = Read(viewer.StudioTexture);
                        v.off();
                        Measure(tex, mask, refTex, $"studio_{key}_{skin}_y{yaw:0}_{v.name}");
                        if (refTex == null) refTex = tex; else Object.DestroyImmediate(tex);
                    }
                    if (refTex != null) Object.DestroyImmediate(refTex);
                    Object.DestroyImmediate(mask);
                }
            }
            Object.DestroyImmediate(host);
        }

        /// <summary>
        /// THE SELF-TEST'S MEASURE (LifeSimSelfTest.CarDots): one car on the
        /// showroom stage (its own key light and fill, the flank turned away from
        /// the key in shade) at <paramref name="yaw"/>, drawn once with the
        /// player's SSAO on or off and the fix on or off; the paint's local
        /// contrast as <see cref="Contrast"/> returns it. No scene is opened:
        /// the stage is made and taken down in the current one.
        /// </summary>
        public static Vector3 StudioContrast(CarModelDef def, int skin, float yaw, bool fix, out int paintPx, out Vector3 plain)
        {
            paintPx = 0; plain = Vector3.zero;
            var canvasGo = new GameObject("CD_Canvas");
            var host = new GameObject("CD_Viewer");
            bool fixWas = CarDots.On;
            try
            {
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var panelGo = new GameObject("CD_Panel", typeof(RectTransform));
                panelGo.transform.SetParent(canvasGo.transform, false);
                var panel = (RectTransform)panelGo.transform;
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.sizeDelta = new Vector2(1280f, 600f);
                var viewer = host.AddComponent<CarViewer>();
                viewer.Show(def, skin);
                viewer.AttachStudio(panel);
                viewer.ResetStudioAngle();
                viewer.TurnBy(yaw - viewer.Yaw);
                viewer.Tick(0f, 0f);
                PlayerAO.Set(false);
                Shader.SetGlobalFloat("_PSXPaintDebug", 12f);
                viewer.RenderNow();
                var mask = Read(viewer.StudioTexture);
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                viewer.RenderNow();
                var refTex = Read(viewer.StudioTexture);
                PlayerAO.Restore();
                Fix(fix);
                PlayerAO.Set(true);
                viewer.RenderNow();
                var tex = Read(viewer.StudioTexture);
                var m = mask.GetPixels32();
                plain = Contrast(tex.GetPixels32(), m, null, tex.width, tex.height, out paintPx, out _);
                var st = Contrast(tex.GetPixels32(), m, refTex.GetPixels32(), tex.width, tex.height, out _, out _);
                Object.DestroyImmediate(tex); Object.DestroyImmediate(mask); Object.DestroyImmediate(refTex);
                return st;
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
                PlayerAO.Restore();
                Fix(fixWas);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(canvasGo);
            }
        }

        public static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        // ------------------------------------------------------------------
        //  One frame per variant, through the PC picture
        // ------------------------------------------------------------------
        static void Shoot(Camera cam, PSXCameraOutput output, Renderer body, string name)
        {
            // the paint mask: debug 12 (paint white, glass / wheel not), raw, no AO
            PlayerAO.Set(false);
            Shader.SetGlobalFloat("_PSXPaintDebug", 12f);
            var mask = Render(cam, output, false);
            Shader.SetGlobalFloat("_PSXPaintDebug", 0f);
            PlayerAO.Restore();
            Texture2D refTex = null;
            foreach (var v in Chosen())
            {
                v.on();
                var tex = Render(cam, output, !rawOnly);
                v.off();
                Measure(tex, mask, refTex, name + "_" + v.name);
                if (refTex == null) refTex = tex; else Object.DestroyImmediate(tex);
            }
            if (refTex != null) Object.DestroyImmediate(refTex);
            Object.DestroyImmediate(mask);
        }

        public static Texture2D Render(Camera cam, PSXCameraOutput output, bool shown)
        {
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            for (int k = 0; k < 2; k++)
            {
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else { var keep = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = keep; }
            }
            var src = rt;
            RenderTexture dst = null;
            var mat = shown && output != null && output.display != null ? output.display.material : null;
            if (mat != null)
            {
                var tmp = new Material(mat) { hideFlags = HideFlags.HideAndDontSave };
                if (tmp.HasProperty("_ColorDepth")) tmp.SetFloat("_ColorDepth", 8f);
                if (tmp.HasProperty("_DitherStrength")) tmp.SetFloat("_DitherStrength", 1f);
                if (tmp.HasProperty("_Fxaa")) tmp.SetFloat("_Fxaa", 1f);
                if (tmp.HasProperty("_Grade")) tmp.SetFloat("_Grade", 1f);
                dst = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                dst.Create();
                var glow = Halation.Wanted(tmp) ? Halation.BuildNow(src) : null;
                Halation.Apply(tmp, glow);
                Graphics.Blit(src, dst, tmp);
                Halation.Release(ref glow);
                Object.DestroyImmediate(tmp);
                src = dst;
            }
            var t = Read(src);
            rt.Release(); Object.DestroyImmediate(rt);
            if (dst != null) { dst.Release(); Object.DestroyImmediate(dst); }
            return t;
        }

        // ------------------------------------------------------------------
        //  The measure: dark dots on the paint
        // ------------------------------------------------------------------
        public const float DotStep = 0.035f;   // display luma below the 9x9 blur
        public const int Box = 4;              // 9x9

        /// <summary>Local contrast of the paint: over the paint mask eroded by
        /// <see cref="Box"/>+2 px, each pixel's display luma minus its 9x9 mean.
        /// Returns (rms, dots per 10k paint px, darkest residual).</summary>
        public static Vector3 Contrast(Color32[] img, Color32[] mask, Color32[] refImg, int w, int h, out int n, out RectInt worst)
        {
            var L = new float[w * h];
            var M = new bool[w * h];
            for (int i = 0; i < L.Length; i++)
            {
                var c = img[i];
                L[i] = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
                if (refImg != null) { var q = refImg[i]; L[i] -= (0.2126f * q.r + 0.7152f * q.g + 0.0722f * q.b) / 255f; }
                var m = mask[i];
                M[i] = m.r > 200 && m.g > 200 && m.b > 200;
            }
            // integral images of L and of the mask
            var S = new double[(w + 1) * (h + 1)];
            var C = new int[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                double row = 0; int rc = 0;
                for (int x = 0; x < w; x++)
                {
                    row += L[y * w + x]; rc += M[y * w + x] ? 1 : 0;
                    S[(y + 1) * (w + 1) + x + 1] = S[y * (w + 1) + x + 1] + row;
                    C[(y + 1) * (w + 1) + x + 1] = C[y * (w + 1) + x + 1] + rc;
                }
            }
            int R = Box + 2, side = 2 * R + 1;
            double sum2 = 0; int dots = 0; n = 0; float darkest = 0f;
            var dotGrid = new Dictionary<int, int>();
            const int cell = 96;
            for (int y = R; y < h - R; y++)
                for (int x = R; x < w - R; x++)
                {
                    int i = y * w + x;
                    if (!M[i]) continue;
                    int x0 = x - R, y0 = y - R, x1 = x + R + 1, y1 = y + R + 1;
                    int cnt = C[y1 * (w + 1) + x1] - C[y0 * (w + 1) + x1] - C[y1 * (w + 1) + x0] + C[y0 * (w + 1) + x0];
                    if (cnt < side * side) continue;   // only deep inside the paint (no edge, no glass, no wheel)
                    int bx0 = x - Box, by0 = y - Box, bx1 = x + Box + 1, by1 = y + Box + 1;
                    double s = S[by1 * (w + 1) + bx1] - S[by0 * (w + 1) + bx1] - S[by1 * (w + 1) + bx0] + S[by0 * (w + 1) + bx0];
                    float res = L[i] - (float)(s / ((2 * Box + 1) * (2 * Box + 1)));
                    sum2 += res * res; n++;
                    if (res < darkest) darkest = res;
                    if (res < -DotStep)
                    {
                        dots++;
                        int key = (y / cell) * 1000 + x / cell;
                        dotGrid.TryGetValue(key, out int k); dotGrid[key] = k + 1;
                    }
                }
            worst = new RectInt(0, 0, 0, 0);
            if (dotGrid.Count > 0)
            {
                var best = dotGrid.OrderByDescending(kv => kv.Value).First().Key;
                worst = new RectInt((best % 1000) * cell, (best / 1000) * cell, cell, cell);
            }
            float rms = n > 0 ? (float)System.Math.Sqrt(sum2 / n) : 0f;
            return new Vector3(rms, n > 0 ? dots * 10000f / n : 0f, darkest);
        }

        static RectInt fixedCrop;
        static string refName;
        static void SaveCrop(Texture2D t, string name)
        {
            var px = t.GetPixels(fixedCrop.x, fixedCrop.y, fixedCrop.width, fixedCrop.height);
            var crop = new Texture2D(fixedCrop.width, fixedCrop.height, TextureFormat.RGB24, false);
            crop.SetPixels(px); crop.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + "_crop.png"), crop.EncodeToPNG());
            Object.DestroyImmediate(crop);
        }
        static void Measure(Texture2D tex, Texture2D mask, Texture2D refTex, string name)
        {
            int w = tex.width, h = tex.height;
            var img = tex.GetPixels32(); var m = mask.GetPixels32();
            var st = Contrast(img, m, null, w, h, out int n, out var worst);
            string speck = "";
            if (refTex != null)
            {
                var sp = Contrast(img, m, refTex.GetPixels32(), w, h, out _, out var worstS);
                speck = $"  | SPECK vs first: rms {sp.x * 255f:0.00}  dots {sp.y,6:0.0}/10k  darkest {sp.z * 255f:0}";
                if (sp.y > 0.5f) worst = worstS;
            }
            log.AppendLine($"  {name,-44} paint px {n,8}  rms {st.x * 255f:0.00}/255  dots {st.y,6:0.0}/10k  darkest {st.z * 255f:0}{speck}");
            // a 1:1 crop (360 px) round the speck's worst cell, placed by the first variant
            // measured against the reference; the reference's own crop is written then, at the same place
            int cs = Mathf.Min(360, Mathf.Min(w, h));
            if (refTex == null) { fixedCrop = new RectInt(0, 0, 0, 0); refName = name; }
            else
            {
                if (fixedCrop.width == 0)
                {
                    var c = worst.width > 0 ? worst.center : new Vector2(w / 2f, h / 2f);
                    fixedCrop = new RectInt(Mathf.Clamp((int)c.x - cs / 2, 0, w - cs), Mathf.Clamp((int)c.y - cs / 2, 0, h - cs), cs, cs);
                    SaveCrop(refTex, refName);
                }
                SaveCrop(tex, name);
            }
            // the whole frame, small
            int sw = w / 4, sh = h / 4;
            var small = new RenderTexture(sw, sh, 0);
            Graphics.Blit(tex, small);
            var st2 = Read(small);
            File.WriteAllBytes(Path.Combine(outDir, name + "_frame.jpg"), st2.EncodeToJPG(85));
            Object.DestroyImmediate(st2); small.Release(); Object.DestroyImmediate(small);
        }
    }
}
