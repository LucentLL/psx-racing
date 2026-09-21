using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Dumps the procedurally generated control sprites to PNGs so the artwork
    /// can be checked without booting the game on a phone. The wheel texture is
    /// drawn pixel by pixel from polar maths, which is exactly the kind of code
    /// that compiles perfectly and renders a smear — so it is worth looking at.
    ///
    /// Editor-only, and reads the generators through reflection so shipping code
    /// does not grow a public API purely for a screenshot.
    /// </summary>
    public static class TouchControlsPreview
    {
        /// <summary>
        /// Render the ASSEMBLED control panel at known control values, so the
        /// direction each gauge moves can be checked without a phone.
        ///
        /// Dumping the sprites was never enough: every reported control bug so
        /// far has been in the wiring or the geometry, not the artwork. A panel
        /// shot with the throttle at 0.75 and the handbrake at 0.4 answers "does
        /// the fill grow the right way" directly.
        /// </summary>
        [MenuItem("PSX Racing/Preview Touch Control Panel")]
        public static void DumpPanel()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            // (label, gas, brake, ebrake, steer)
            var states = new[]
            {
                ("rest", 0f, 0f, 0f, 0f),
                ("gas75_ebrk40", 0.75f, 0f, 0.4f, 0.5f),
                ("brake60", 0f, 0.6f, 0f, -0.5f),
            };

            foreach (var (label, gas, brake, ebrake, steer) in states)
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

                // The touch panel and the cluster both scale off a 1280x720
                // reference, so shooting at exactly that renders every control at
                // its design size — no scale factor to mentally divide out when
                // judging whether a dial is big enough to read.
                const int W = 1280, H = 720;
                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
                cam.orthographic = true;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;

                var host = new GameObject("TouchControls");
                var tc = host.AddComponent<TouchControls>();
                tc.forceShow = true;
                // Edit mode does not run lifecycle callbacks, so Awake — which is
                // where the whole control panel is built — has to be called by
                // hand. Same reason the menu preview reflects into Start().
                Invoke(tc, "Awake");
                Invoke(tc, "SetVisible", true);

                SetPedal(tc, "gasPedal", gas);
                SetPedal(tc, "brakePedal", brake);
                SetPedal(tc, "ebrakePedal", ebrake);
                var wheel = Field<TouchWheel>(tc, "wheel");
                if (wheel != null) wheel.SetVisualAxis(steer);

                // The instrument cluster, on the same kind of canvas the builder
                // gives it. It shares the bottom edge with the wheel and the
                // pedals and is the whole reason those two are pushed into the
                // corners, so a panel shot without it cannot answer the question
                // it exists to answer: does everything fit, and is the dial big
                // enough to read.
                var clusterCanvasGO = new GameObject("ClusterCanvas");
                var cc = clusterCanvasGO.AddComponent<Canvas>();
                cc.renderMode = RenderMode.ScreenSpaceOverlay;
                cc.sortingOrder = 90;
                var cs = clusterCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                cs.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                cs.referenceResolution = new Vector2(1280f, 720f);
                cs.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                cs.matchWidthOrHeight = 0.5f;
                var clusterGO = new GameObject("Cluster", typeof(RectTransform));
                clusterGO.transform.SetParent(clusterCanvasGO.transform, false);
                var crt = (RectTransform)clusterGO.transform;
                crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
                crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                var cluster = clusterGO.AddComponent<GaugeCluster>();
                // No car: the dials fall back to a 8000 rpm redline and a
                // 240 km/h scale, which is a representative cluster and does not
                // need a rigidbody to exist.
                Canvas.ForceUpdateCanvases();
                cluster.Build();
                // Off half scale on purpose, and in opposite directions: a
                // sub-gauge needle points straight down whether or not its
                // sweep is mirrored, so a picture of one at rest proves
                // nothing. Cool-ish coolant leans LEFT, three-quarters of a
                // tank leans RIGHT.
                cluster.PoseSubGauges(0.30f, 0.75f);

                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 10f;
                }
                Canvas.ForceUpdateCanvases();
                // The wheel draws its rim in Update, which does not run here.
                if (wheel != null) Invoke(wheel, "Update");
                Canvas.ForceUpdateCanvases();

                // Every label on this panel came back blank once. Report what
                // the Text components actually hold rather than squinting at
                // the PNG: a null font and a correctly-built label that simply
                // did not rasterise look identical in a picture.
                int texts = 0, noFont = 0, noText = 0, clear = 0;
                foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                {
                    texts++;
                    if (t.font == null) noFont++;
                    if (string.IsNullOrEmpty(t.text)) noText++;
                    if (t.color.a < 0.05f) clear++;
                }
                // An ERROR, not a note. A blank label is invisible in the PNG and
                // indistinguishable from a label that simply has a dark
                // background — which is exactly how "GAS / BRAKE / CAM / RESET
                // are blank" survived a pass that was specifically about making
                // these buttons legible.
                if (noFont > 0 || noText > 0 || clear > 0)
                    Debug.LogError($"[Preview] BLANK LABELS: {texts} total, {noFont} with no " +
                                   $"font, {noText} empty, {clear} transparent");
                else
                    Debug.Log($"[Preview] labels: {texts} total, all captioned and visible");

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                string path = Path.Combine(outDir, "controls_" + label + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[Preview] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>
        /// The instrument cluster ON ITS OWN, with no touch panel in the scene.
        ///
        /// That is a different layout, not the same one with the controls
        /// hidden: with no wheel and no pedals the dials go to the bottom
        /// CORNERS instead of into the band between them, and the gear gets a
        /// panel of its own because there is no shifter knob carrying it. It is
        /// what a PC player looks at, and DumpPanel — which forces the touch
        /// controls on — can never show it.
        ///
        /// WHAT IT IS DRAWN OVER, AND AT WHAT HOUR (2026-09-21, the smoked
        /// faces). Two optional environment variables:
        ///
        ///   PSX_CLUSTER_HOUR      an hour index 0-6 (or its name, "NIGHT"),
        ///                         or a list of them ("2,6"). Applied with
        ///                         TimeOfDay.Apply before the cluster is built,
        ///                         so the palette follows it: ClusterBulbs
        ///                         .Backlit reads TimeOfDay.Current. Output
        ///                         names get "_hour_&lt;h&gt;_&lt;name&gt;".
        ///   PSX_CLUSTER_BACKDROP  a PNG, or a ';'-separated list paired with
        ///                         the hours in order, drawn full-screen BEHIND
        ///                         the cluster instead of the flat background.
        ///                         A bare file name is looked for in Screenshots
        ///                         first, where the screenshot tool leaves its
        ///                         psx_hour_*.png frames.
        ///
        /// Without them the tool renders exactly what it always did. It needs
        /// them because a see-through dial cannot be judged over flat
        /// near-black: black-on-black is legible by construction, and the case
        /// the halos exist for is a noon sky or a sodium-lit road in the
        /// bottom corners. And because no flat-colour tool ever applied an
        /// hour, every cluster preview so far has shown the lit NIGHT palette
        /// (TimeOfDay.Current starts at Sunset, which runs its lights): the day
        /// dial, white on smoke, had never been looked at.
        /// </summary>
        [MenuItem("PSX Racing/Preview Gauge Cluster")]
        public static void DumpCluster()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            // (label, rpm, speed, coolant, fuel). Two readings rather than one,
            // and both off the ends and off half scale: an end stop shows how
            // far the sweep runs but not which way round it goes, and half
            // scale shows neither.
            var states = new[]
            {
                ("idle", 900f, 0f, 0.12f, 0.92f),
                ("drive", 5200f, 84f, 0.46f, 0.28f),
            };

            var passes = ClusterPasses(outDir, out bool nameBackdrops);
            var backdrops = new Dictionary<string, Texture2D>();
            int hourBefore = TimeOfDay.Current;
            bool hourApplied = false;

            foreach (var (hour, backdropPath) in passes)
            foreach (var (label, rpm, speed, coolant, fuel) in states)
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

                // The hour BEFORE the cluster is built, and after the scene it
                // writes its sky into exists. A cluster bakes its palette at
                // Build and only rebakes when the bulb's revision changes,
                // which it never gets the chance to do here.
                if (hour >= 0)
                {
                    TimeOfDay.Apply(hour, null);
                    hourApplied = true;
                    Debug.Log($"[Preview] hour {hour} {TimeOfDay.At(hour).name}: " +
                              $"bulb backlit = {ClusterBulbs.Backlit}");
                }

                const int W = 1280, H = 720;
                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
                cam.orthographic = true;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;

                var backdrop = LoadBackdrop(backdropPath, backdrops);
                if (backdrop != null) AddBackdrop(cam, backdrop, W, H);

                var clusterCanvasGO = new GameObject("ClusterCanvas");
                var cc = clusterCanvasGO.AddComponent<Canvas>();
                cc.renderMode = RenderMode.ScreenSpaceOverlay;
                cc.sortingOrder = 90;
                var cs = clusterCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                cs.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                cs.referenceResolution = new Vector2(1280f, 720f);
                cs.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                cs.matchWidthOrHeight = 0.5f;
                var clusterGO = new GameObject("Cluster", typeof(RectTransform));
                clusterGO.transform.SetParent(clusterCanvasGO.transform, false);
                var crt = (RectTransform)clusterGO.transform;
                crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
                crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                var cluster = clusterGO.AddComponent<GaugeCluster>();
                Canvas.ForceUpdateCanvases();
                cluster.Build();
                cluster.PoseNeedles(rpm, speed);
                cluster.PoseSubGauges(coolant, fuel);

                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 10f;
                }
                Canvas.ForceUpdateCanvases();

                int texts = 0, blank = 0;
                foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                {
                    texts++;
                    if (t.font == null || string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) blank++;
                }
                if (blank > 0)
                    Debug.LogError($"[Preview] BLANK LABELS: {blank} of {texts}");
                else
                    Debug.Log($"[Preview] labels: {texts} total, all captioned and visible");

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                // The hour in the name, so a noon run and a night run sit side
                // by side instead of overwriting each other; the backdrop's
                // own name as well only when there are more backdrops than
                // hours to tell them apart by.
                string suffix = hour >= 0
                    ? "_hour_" + hour + "_" + TimeOfDay.At(hour).name.ToLowerInvariant() : "";
                if (nameBackdrops && !string.IsNullOrEmpty(backdropPath))
                    suffix += "_" + Path.GetFileNameWithoutExtension(backdropPath);
                string path = Path.Combine(outDir, "cluster_" + label + suffix + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[Preview] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            foreach (var t in backdrops.Values)
                if (t != null) Object.DestroyImmediate(t);
            // Put the hour back. TimeOfDay.Current is static and outlives the
            // scenes this made. Left at NOON, it would hand the next preview
            // run in the same editor a day palette it never asked for.
            if (hourApplied) TimeOfDay.Apply(hourBefore, null);
        }

        /// <summary>
        /// The (hour, backdrop) pairs DumpCluster renders, from
        /// PSX_CLUSTER_HOUR and PSX_CLUSTER_BACKDROP. The longer list sets the
        /// count and the shorter one repeats its last entry. With neither set
        /// it is one pass of (-1, null): no hour applied, the flat background,
        /// the tool as it always was.
        /// </summary>
        static List<(int hour, string backdrop)> ClusterPasses(string shotsDir, out bool nameBackdrops)
        {
            var hours = new List<int>();
            string hourEnv = System.Environment.GetEnvironmentVariable("PSX_CLUSTER_HOUR");
            if (!string.IsNullOrWhiteSpace(hourEnv))
                foreach (var raw in hourEnv.Split(new[] { ',', ';', ' ' },
                                                  System.StringSplitOptions.RemoveEmptyEntries))
                {
                    int h = HourIndex(raw.Trim());
                    if (h >= 0) hours.Add(h);
                    else Debug.LogError($"[Preview] PSX_CLUSTER_HOUR: '{raw}' is not an hour " +
                                        $"(0-{TimeOfDay.Count - 1} or a name such as NIGHT)");
                }

            var files = new List<string>();
            string bdEnv = System.Environment.GetEnvironmentVariable("PSX_CLUSTER_BACKDROP");
            if (!string.IsNullOrWhiteSpace(bdEnv))
                foreach (var raw in bdEnv.Split(new[] { ';', '|' },
                                                System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = raw.Trim().Trim('"');
                    if (p.Length > 0) files.Add(ResolveBackdrop(p, shotsDir));
                }

            nameBackdrops = files.Count > 1 && files.Count > hours.Count;
            int n = Mathf.Max(1, Mathf.Max(hours.Count, files.Count));
            var passes = new List<(int, string)>(n);
            for (int i = 0; i < n; i++)
                passes.Add((hours.Count > 0 ? hours[Mathf.Min(i, hours.Count - 1)] : -1,
                            files.Count > 0 ? files[Mathf.Min(i, files.Count - 1)] : null));
            return passes;
        }

        /// <summary>An hour by index ("6") or by its table name ("night"),
        /// -1 if it is neither.</summary>
        static int HourIndex(string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                return h >= 0 && h < TimeOfDay.Count ? h : -1;
            for (int i = 0; i < TimeOfDay.Count; i++)
                if (string.Equals(TimeOfDay.At(i).name, s, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>A backdrop path as given if it is rooted; otherwise
        /// looked for in Screenshots, where the frames it is meant for are
        /// written, and then in the project folder.</summary>
        static string ResolveBackdrop(string p, string shotsDir)
        {
            if (Path.IsPathRooted(p)) return p;
            string inShots = Path.Combine(shotsDir, p);
            if (File.Exists(inShots)) return inShots;
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, p);
        }

        /// <summary>
        /// Load a backdrop PNG once per run. Misses are cached too: a path
        /// that failed is reported once and the pass falls back to the flat
        /// background rather than stopping the run.
        ///
        /// sRGB and point-filtered. The frame it holds is the game's picture
        /// as the display shows it, so it must come back out of the sRGB
        /// render target byte for byte; and a 240-line frame is shown with
        /// hard pixels, which is how the dials will be seen over it.
        /// </summary>
        static Texture2D LoadBackdrop(string path, Dictionary<string, Texture2D> cache)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // A cached MISS is a real null and is returned as one. A cached
            // texture that something has since DESTROYED is only null by
            // Unity's ==, and is loaded again rather than silently dropping
            // the backdrop from every state after the first.
            if (cache.TryGetValue(path, out var hit) && ((object)hit == null || hit != null))
                return hit;
            Texture2D tex = null;
            if (!File.Exists(path))
                Debug.LogError("[Preview] PSX_CLUSTER_BACKDROP: no file at " + path);
            else
            {
                // HideAndDontSave, which includes DontUnloadUnusedAsset: every
                // state opens a NEW scene, and a loose texture that no scene
                // references is exactly what a scene change may unload.
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (tex.LoadImage(File.ReadAllBytes(path)))
                {
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Debug.Log($"[Preview] backdrop {path} ({tex.width}x{tex.height})");
                }
                else
                {
                    Debug.LogError("[Preview] PSX_CLUSTER_BACKDROP: not an image: " + path);
                    Object.DestroyImmediate(tex);
                    tex = null;
                }
            }
            cache[path] = tex;
            return tex;
        }

        /// <summary>
        /// The backdrop: one full-frame RawImage on a canvas of its own,
        /// already in camera space and BEHIND the cluster. It is further from
        /// the lens (20 against the cluster's 10) and lower in sort order, so
        /// both ways of ordering transparent UI agree. It is not an overlay
        /// canvas, so the loop that moves the cluster's overlay canvases onto
        /// the camera leaves it alone.
        ///
        /// Cropped to COVER the frame from the centre rather than stretched: a
        /// screenshot of another aspect squashed to 16:9 would make every
        /// verge and lamp the wrong shape. The bottom corners the dials sit in
        /// survive a centred crop of any sensible frame.
        /// </summary>
        static void AddBackdrop(Camera cam, Texture2D tex, int w, int h)
        {
            var canvasGO = new GameObject("BackdropCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 20f;
            canvas.sortingOrder = -100;

            var imgGO = new GameObject("Backdrop", typeof(RectTransform));
            imgGO.transform.SetParent(canvasGO.transform, false);
            var rt = (RectTransform)imgGO.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = imgGO.AddComponent<UnityEngine.UI.RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            float ta = tex.width / (float)Mathf.Max(1, tex.height), sa = w / (float)h;
            img.uvRect = ta > sa
                ? new Rect((1f - sa / ta) * 0.5f, 0f, sa / ta, 1f)
                : new Rect(0f, (1f - ta / sa) * 0.5f, 1f, ta / sa);
        }

        static void SetPedal(TouchControls tc, string field, float amount)
        {
            var pedal = Field<TouchPedal>(tc, field);
            if (pedal == null) { Debug.LogError("[Preview] no pedal " + field); return; }
            // Poses through the display path, which is what the gauge draws.
            // Writing Amount would set the value the CAR reads and leave the
            // gauge at zero — and keeping those two separable is the entire
            // point of the stuck-brake fix.
            pedal.SetVisualAmount(amount);
        }

        static T Field<T>(object obj, string name) where T : class =>
            obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
               ?.GetValue(obj) as T;

        static void Invoke(object obj, string method, params object[] args) =>
            obj.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
               ?.Invoke(obj, args);

        [MenuItem("PSX Racing/Preview Touch Control Art")]
        public static void Dump()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            foreach (string name in new[] { "Wheel", "Circle", "Rounded" })
            {
                var m = typeof(TouchControls).GetMethod(name,
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) { Debug.LogError("[Preview] no method " + name); continue; }

                var sprite = m.Invoke(null, null) as Sprite;
                if (sprite == null) { Debug.LogError("[Preview] null sprite " + name); continue; }

                var src = sprite.texture;
                // Composite onto a mid grey: these sprites are mostly alpha, and
                // dark-on-transparent tells you nothing in an image viewer.
                var flat = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
                var px = src.GetPixels();
                var outPx = new Color[px.Length];
                var bg = new Color(0.35f, 0.36f, 0.40f);
                for (int i = 0; i < px.Length; i++)
                    outPx[i] = Color.Lerp(bg, new Color(px[i].r, px[i].g, px[i].b), px[i].a);
                flat.SetPixels(outPx);
                flat.Apply();

                string path = Path.Combine(outDir, "control_" + name.ToLower() + ".png");
                File.WriteAllBytes(path, flat.EncodeToPNG());
                Object.DestroyImmediate(flat);
                Debug.Log("[Preview] wrote " + path + " (" + src.width + "x" + src.height + ")");
            }
        }
    }
}
