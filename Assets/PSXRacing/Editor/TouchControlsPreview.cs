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

            foreach (var (label, rpm, speed, coolant, fuel) in states)
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

                const int W = 1280, H = 720;
                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
                cam.orthographic = true;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;

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

                string path = Path.Combine(outDir, "cluster_" + label + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[Preview] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
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
