using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The driving HUD a PHONE player sees — the touch panel, the two dials
    /// and the speed-streak overlay, composed on one frame — at the three
    /// aspects the game is played at, with the placement CHECKED in pixels
    /// rather than eyeballed.
    ///
    /// Three pieces on three canvases, and the reason this tool exists is that
    /// no earlier one put them together honestly. TouchControlsPreview.DumpPanel
    /// renders the panel and the cluster at one size, and builds the cluster
    /// BEFORE it pins the canvas scale — so the dials measured a canvas sized
    /// off the batchmode editor window and the picture showed a layout nobody
    /// plays. Here every canvas is pointed at the camera and pinned to the
    /// scale its own CanvasScaler would reach on that screen FIRST, and only
    /// then is anything asked where it is.
    ///
    /// And the streaks are woken in the order that shipped them as white bars:
    /// HudOnTop.Apply over the HUD canvas before SpeedLines.Awake — RaceHUD
    /// winning the race — so a picture of radial streaks is a picture of the
    /// fix, not of a lucky order.
    /// </summary>
    public static class DriveHudPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            // The owner's own screenshot (2026-09-18), pixel for pixel.
            ("phone_wide", 2000, 923),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        [MenuItem("PSX Racing/Preview Driving HUD (touch)")]
        public static void Capture()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            int fails = 0;
            foreach (var s in Sizes) fails += Shoot(outDir, s.name, s.w, s.h);

            Debug.Log(fails == 0 ? "[DriveHud] ALL CHECKS OK"
                                 : "[DriveHud] FAIL — " + fails + " check(s) failed");
            Debug.Log("[DriveHud] done");
        }

        static int Shoot(string outDir, string label, int w, int h)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int fails = 0;

            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // A hazy mid tone, so white streaks and dark dials both show.
            cam.backgroundColor = new Color(0.40f, 0.42f, 0.47f);
            cam.orthographic = true;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;

            // ---- the streak overlay, the way the builder wires it ----------
            var hudGO = new GameObject("HUDCanvas");
            var hud = hudGO.AddComponent<Canvas>();
            hud.renderMode = RenderMode.ScreenSpaceCamera;
            hud.worldCamera = cam;
            hud.planeDistance = 20f;          // behind the two panels
            hud.sortingOrder = 0;
            var linesGO = new GameObject("SpeedLines", typeof(RectTransform));
            linesGO.transform.SetParent(hudGO.transform, false);
            var lrt = (RectTransform)linesGO.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var img = linesGO.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PSXRacingBuilder.SpeedStreaksTexPath);
            var saved = AssetDatabase.LoadAssetAtPath<Material>(WorldKit.MatDir + "/SpeedLines.mat");
            var streakShader = Shader.Find(SpeedLines.ShaderName);
            img.material = saved != null ? saved
                         : streakShader != null ? new Material(streakShader) : null;
            img.enabled = false;
            if (img.texture == null)
                fails += Fail(label, "no streak texture at " + PSXRacingBuilder.SpeedStreaksTexPath +
                                     " (run the scene build)");

            // RaceHUD.Awake FIRST — the order that drew the bars.
            HudOnTop.Apply(hudGO);
            var lines = linesGO.AddComponent<SpeedLines>();
            lines.image = img;
            lines.cam = cam;
            Invoke(lines, "Awake");
            var m = img.material;
            if (m == null || m.shader == null || m.shader.name != SpeedLines.ShaderName)
                fails += Fail(label, "STREAKS ON " + (m == null || m.shader == null ? "NO" : m.shader.name) +
                                     " — the overlay would draw its raw sheet as white bars");
            else
            {
                // Full strength, which is the most it can ever cover.
                m.SetFloat("_Intensity", SpeedLines.MaxIntensity);
                m.SetFloat("_Scroll", 0.37f);
                m.SetFloat("_Aspect", w / (float)h);
                img.enabled = true;
                Ok(label, "streaks drawn by " + m.shader.name + " after HudOnTop had its turn");
            }

            // ---- the touch panel ----------------------------------------
            var host = new GameObject("TouchControls");
            var tc = host.AddComponent<TouchControls>();
            tc.forceShow = true;
            // Edit mode runs no lifecycle callbacks; Awake is where the whole
            // panel is built.
            Invoke(tc, "Awake");
            Invoke(tc, "SetVisible", true);
            // Posed the way the owner's screenshot was: foot down, fourth.
            SetPedal(tc, "gasPedal", 0.85f);
            var shifter = Field<TouchShifter>(tc, "shifter");
            if (shifter != null) shifter.SetGear(4);

            // ---- the cluster, on the canvas the builder gives it ---------
            var clusterCanvasGO = new GameObject("ClusterCanvas");
            var cc = clusterCanvasGO.AddComponent<Canvas>();
            cc.renderMode = RenderMode.ScreenSpaceOverlay;
            cc.sortingOrder = 90;
            var cs = clusterCanvasGO.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1280f, 720f);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;
            var clusterGO = new GameObject("Cluster", typeof(RectTransform));
            clusterGO.transform.SetParent(clusterCanvasGO.transform, false);
            var crt = (RectTransform)clusterGO.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var cluster = clusterGO.AddComponent<GaugeCluster>();

            // Pin BEFORE the cluster measures itself — see the class comment.
            Repoint(cam, w, h);
            Canvas.ForceUpdateCanvases();
            cluster.Build();
            cluster.PoseNeedles(4200f, 112f);
            cluster.PoseSubGauges(0.30f, 0.90f);
            Canvas.ForceUpdateCanvases();
            var wheel = Field<TouchWheel>(tc, "wheel");
            if (wheel != null) Invoke(wheel, "Update");   // it draws its rim in Update
            Canvas.ForceUpdateCanvases();

            // ---- measure, in pixels of this frame ------------------------
            var panel = host.transform.Find("TouchCanvas");
            var wheelR = PixelRect(cam, panel, "SteerWheel");
            var gasPip = PixelRect(cam, panel, "Gas/Level");
            var ebrakePip = PixelRect(cam, panel, "Handbrake/Level");
            var brakePip = PixelRect(cam, panel, "Brake/Level");
            var shift = PixelRect(cam, panel, "Shifter");
            var tach = PixelRect(cam, clusterGO.transform, "Tach");
            var speedo = PixelRect(cam, clusterGO.transform, "Speedo");
            if (!wheelR.HasValue || !gasPip.HasValue || !ebrakePip.HasValue || !brakePip.HasValue ||
                !shift.HasValue || !tach.HasValue || !speedo.HasValue)
            {
                fails += Fail(label, "a control or a dial is missing — nothing measured");
            }
            else
            {
                float leftGap = wheelR.Value.xMin;
                float rightGap = w - Mathf.Max(gasPip.Value.xMax, ebrakePip.Value.xMax);
                if (Mathf.Abs(leftGap - rightGap) <= 1.5f)
                    Ok(label, "wheel " + leftGap.ToString("0") + " px from the left edge, pedal column " +
                              rightGap.ToString("0") + " px from the right");
                else
                    fails += Fail(label, "EDGES DO NOT MATCH — wheel " + leftGap.ToString("0") +
                                         " px in, pedal column " + rightGap.ToString("0") + " px in");

                float wheelClear = tach.Value.xMin - wheelR.Value.xMax;
                float pedalEdge = Mathf.Min(brakePip.Value.xMin, shift.Value.xMin);
                float pedalClear = pedalEdge - speedo.Value.xMax;
                if (wheelClear >= -0.5f) Ok(label, "rev counter " + wheelClear.ToString("0") + " px clear of the wheel");
                else fails += Fail(label, "REV COUNTER OVERLAPS THE WHEEL by " + (-wheelClear).ToString("0") + " px");
                if (pedalClear >= -0.5f) Ok(label, "speedometer " + pedalClear.ToString("0") + " px clear of the pedals");
                else fails += Fail(label, "SPEEDOMETER OVERLAPS THE PEDALS by " + (-pedalClear).ToString("0") + " px");

                float mid = w * 0.5f;
                if (tach.Value.xMax < mid && speedo.Value.xMin > mid)
                    Ok(label, "the middle is clear: " + (speedo.Value.xMin - tach.Value.xMax).ToString("0") +
                              " px between the dials (" +
                              ((speedo.Value.xMin - tach.Value.xMax) / w * 100f).ToString("0") +
                              "% of the width), round the centre line");
                else
                    fails += Fail(label, "A DIAL SITS ACROSS THE CENTRE — tach to " +
                                         tach.Value.xMax.ToString("0") + ", speedo from " +
                                         speedo.Value.xMin.ToString("0") + ", centre " + mid.ToString("0"));
            }

            // ---- are the streaks STREAKS? ------------------------------
            // A frame without the overlay and one with it, differenced. The
            // overlay has now failed two ways: drawn flat as white bars (the
            // material check above), and — hidden behind that — drawn by the
            // right shader reading a mask that was on everywhere, a smooth
            // white wash round the frame. Both look like "something is there",
            // so what is measured is HOW MUCH of the ring lights: a wash lights
            // most of it, streaks a few percent (the sheet is ~5.6% lit).
            if (img.enabled)
            {
                img.enabled = false;
                var bare = Grab(cam, rt, w, h);
                img.enabled = true;
                var lit = Grab(cam, rt, w, h);
                int n = 0, on = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // The top strip and the upper left edge: in the ring on
                        // every aspect, and clear of every control and dial.
                        bool top = y >= h * 0.80f && y < h * 0.98f && x >= w * 0.02f && x < w * 0.98f;
                        bool side = y >= h * 0.50f && y < h * 0.80f && x >= w * 0.01f && x < w * 0.12f;
                        if (!top && !side) continue;
                        n++;
                        int i = y * w + x;
                        if (Luma(lit[i]) - Luma(bare[i]) > 0.08f) on++;
                    }
                float frac = n > 0 ? on / (float)n : 0f;
                if (on < 30)
                    fails += Fail(label, "NO STREAKS DRAWN in the ring (" + on + " px lit)");
                else if (frac > 0.15f)
                    fails += Fail(label, "THE RING IS A WASH, not streaks: " + (frac * 100f).ToString("0") +
                                         "% of it lit — the shader's mask is on everywhere");
                else
                    Ok(label, "streaks, not a wash: " + (frac * 100f).ToString("0.0") + "% of the ring lit (" +
                              on + " px) at full strength");
            }

            Snap(cam, rt, w, h, Path.Combine(outDir, "drivehud_" + label + ".png"));
            return fails;
        }

        static Color32[] Grab(Camera cam, RenderTexture rt, int w, int h)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        // ------------------------------------------------------------------

        /// <summary>
        /// Every overlay canvas onto the camera, at the scale its OWN
        /// CanvasScaler would reach on a w x h screen — read off the component,
        /// so the picture is the layout the player gets rather than one this
        /// tool made up. Both panels use ScaleWithScreenSize at 1280x720 with a
        /// 50/50 match; if either ever stops matching the other, this still
        /// draws each one honestly.
        /// </summary>
        static void Repoint(Camera cam, int w, int h)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f;
                var s = c.GetComponent<CanvasScaler>();
                if (s == null) continue;
                float factor = 1f;
                if (s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    var r = s.referenceResolution;
                    factor = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / r.x, 2f),
                                                      Mathf.Log(h / r.y, 2f),
                                                      s.matchWidthOrHeight));
                }
                else if (s.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                    factor = s.scaleFactor;
                s.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                s.scaleFactor = factor;
            }
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>A child's rect in pixels of the camera's target, or null
        /// when there is no such child.</summary>
        static Rect? PixelRect(Camera cam, Transform root, string path)
        {
            if (root == null) return null;
            var t = root.Find(path) as RectTransform;
            if (t == null) return null;
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static void Snap(Camera cam, RenderTexture rt, int w, int h, string path)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[DriveHud] wrote " + path);
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static void Ok(string label, string what) => Debug.Log("[DriveHud] " + label + " ok   " + what);

        static int Fail(string label, string what)
        {
            Debug.LogError("[DriveHud] " + label + " FAIL " + what);
            return 1;
        }

        static void SetPedal(TouchControls tc, string field, float amount)
        {
            var pedal = Field<TouchPedal>(tc, field);
            if (pedal != null) pedal.SetVisualAmount(amount);
        }

        static T Field<T>(object obj, string name) where T : class =>
            obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
               ?.GetValue(obj) as T;

        static void Invoke(object obj, string method, params object[] args) =>
            obj.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
               ?.Invoke(obj, args);
    }
}
