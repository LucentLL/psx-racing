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
    /// The driving HUD a PHONE player sees — the touch panel and the two
    /// dials, composed on one frame — at the three aspects the game is played
    /// at, with the placement CHECKED in pixels rather than eyeballed.
    ///
    /// Two pieces on two canvases, and the reason this tool exists is that
    /// no earlier one put them together honestly. TouchControlsPreview.DumpPanel
    /// renders the panel and the cluster at one size, and builds the cluster
    /// BEFORE it pins the canvas scale — so the dials measured a canvas sized
    /// off the batchmode editor window and the picture showed a layout nobody
    /// plays. Here every canvas is pointed at the camera and pinned to the
    /// scale its own CanvasScaler would reach on that screen FIRST, and only
    /// then is anything asked where it is.
    ///
    /// The PIZZA CAM panel is in every frame too, since it moved to the right
    /// edge (2026-09-25, "opposite of the race map") where the speedometer
    /// and the pedals are - and one PC frame, no touch panel, because on a PC
    /// the speedometer is in that corner rather than beside the pedals.
    ///
    /// (It used to compose the speed-streak overlay too. The streaks are
    /// gone — the owner's call, 2026-09-19 — and the blur that replaced them
    /// is a pipeline pass with its own instrument: SpeedBlurPreview.)
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

        /// <summary>The desktop race map: RaceHUD's default centre and height,
        /// as fractions of the frame. (The phone's are RaceHUD constants.)</summary>
        const float PcMapCentre = 0.60f, PcMapFrac = 0.30f;

        [MenuItem("PSX Racing/Preview Driving HUD (touch)")]
        public static void Capture()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            int fails = 0;
            foreach (var s in Sizes) fails += Shoot(outDir, s.name, s.w, s.h, true);
            fails += Shoot(outDir, "pc_16x9", 1920, 1080, false);

            Debug.Log(fails == 0 ? "[DriveHud] ALL CHECKS OK"
                                 : "[DriveHud] FAIL — " + fails + " check(s) failed");
            Debug.Log("[DriveHud] done");
        }

        static int Shoot(string outDir, string label, int w, int h, bool touch)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int fails = 0;

            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // A hazy mid tone, so pale controls and dark dials both show.
            cam.backgroundColor = new Color(0.40f, 0.42f, 0.47f);
            cam.orthographic = true;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;

            // ---- the touch panel ----------------------------------------
            GameObject host = null;
            TouchControls tc = null;
            if (touch)
            {
                host = new GameObject("TouchControls");
                tc = host.AddComponent<TouchControls>();
                tc.forceShow = true;
                // Edit mode runs no lifecycle callbacks; Awake is where the whole
                // panel is built.
                Invoke(tc, "Awake");
                Invoke(tc, "SetVisible", true);
                // Posed the way the owner's screenshot was: foot down, fourth.
                SetPedal(tc, "gasPedal", 0.85f);
                var shifter = Field<TouchShifter>(tc, "shifter");
                if (shifter != null) shifter.SetGear(4);
            }

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

            // ---- the pizza cam's panel, on its own canvas as in the game ----
            var pizza = PizzaCam.SpawnPanelForPreview();

            // ---- stand-ins for the race map and the MENU button ------------
            // At the rectangles RaceHUD and PauseMenu compute, so the phone's
            // top-of-screen layout (2026-09-25) is measured, not described.
            // The map lives on the framebuffer canvas, where a fraction of the
            // frame height is the whole story; a canvas with no scaler draws in
            // pixels here.
            float mapFrac = touch ? RaceHUD.TouchMapFrac : PcMapFrac;
            float mapTop = touch ? RaceHUD.TouchMapTopFrac : 1f - PcMapCentre - PcMapFrac * 0.5f;
            var standIns = new GameObject("StandIns");
            var sic = standIns.AddComponent<Canvas>();
            sic.renderMode = RenderMode.ScreenSpaceOverlay;
            sic.sortingOrder = 80;
            StandIn(standIns.transform, "Map", new Vector2(0f, 1f),
                                   new Vector2(4f, -mapTop * h), new Vector2(mapFrac * h, mapFrac * h),
                                   new Color(0.95f, 0.95f, 0.95f, 0.55f));
            var menuCanvasGO = new GameObject("MenuCanvas");
            var mc = menuCanvasGO.AddComponent<Canvas>();
            mc.renderMode = RenderMode.ScreenSpaceOverlay;
            mc.sortingOrder = 200;
            var ms = menuCanvasGO.AddComponent<CanvasScaler>();
            ms.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            ms.referenceResolution = new Vector2(1280f, 720f);
            ms.matchWidthOrHeight = 0.5f;
            Vector2 menuPos = PauseMenu.MenuButtonPos(touch, out Vector2 menuAnchor);
            StandIn(menuCanvasGO.transform, "Menu", menuAnchor, menuPos,
                                    PauseMenu.MenuButtonSize, new Color(1f, 1f, 1f, 0.35f));

            // Pin BEFORE the cluster measures itself — see the class comment.
            Repoint(cam, w, h);
            Canvas.ForceUpdateCanvases();
            cluster.Build();
            cluster.PoseNeedles(4200f, 112f);
            cluster.PoseSubGauges(0.30f, 0.90f);
            Canvas.ForceUpdateCanvases();
            if (tc != null)
            {
                var wheel = Field<TouchWheel>(tc, "wheel");
                if (wheel != null) Invoke(wheel, "Update");   // it draws its rim in Update
            }
            // After the cluster: it stands on what the cluster published.
            pizza.PlaceNow();
            Canvas.ForceUpdateCanvases();

            var mapR = PixelRect(cam, standIns.transform, "Map");
            var menuR = PixelRect(cam, menuCanvasGO.transform, "Menu");
            fails += CheckPizzaCam(cam, label, w, h, pizza, clusterGO.transform,
                                   touch ? host.transform.Find("TouchCanvas") : null, mapR.Value);
            if (touch)
            {
                var wheelBox = PixelRect(cam, host.transform.Find("TouchCanvas"), "SteerWheel");
                float overWheel = menuR.Value.yMin - wheelBox.Value.yMax;
                float underMap = mapR.Value.yMin - menuR.Value.yMax;
                if (overWheel >= -0.5f && underMap >= -0.5f)
                    Ok(label, "MENU middle-left: " + overWheel.ToString("0") + " px over the wheel, " +
                              underMap.ToString("0") + " px under the map (centre " +
                              (menuR.Value.center.y / h * 100f).ToString("0") + "% up)");
                else
                    fails += Fail(label, "MENU COLLIDES - " + overWheel.ToString("0") + " px over the wheel, " +
                                         underMap.ToString("0") + " px under the map");
                if (mapR.Value.yMax >= h * 0.80f)
                    Ok(label, "race map at the top: " + (h - mapR.Value.yMax).ToString("0") + " px under the edge");
                else
                    fails += Fail(label, "RACE MAP NOT AT THE TOP - " + (h - mapR.Value.yMax).ToString("0") + " px down");
            }
            if (!touch)
            {
                Snap(cam, rt, w, h, Path.Combine(outDir, "drivehud_" + label + ".png"));
                return fails;
            }

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

            Snap(cam, rt, w, h, Path.Combine(outDir, "drivehud_" + label + ".png"));
            return fails;
        }

        /// <summary>
        /// The pizza cam: on the right half, level with the race map's band
        /// on the left, above the speedometer, and (on a phone) in off the
        /// pedal column - the four things "opposite of the race map" has to
        /// mean on every screen for it to mean anything.
        /// </summary>
        static int CheckPizzaCam(Camera cam, string label, int w, int h, PizzaCam pizza,
                                 Transform cluster, Transform touchPanel, Rect map)
        {
            int fails = 0;
            var pc = PixelRect(cam, pizza.transform, "PizzaCamCanvas/Panel");
            var speedo = PixelRect(cam, cluster, "Speedo");
            if (!pc.HasValue || !speedo.HasValue)
                return Fail(label, "pizza cam or speedometer missing - nothing measured");
            var r = pc.Value;
            if (r.xMin > w * 0.5f) Ok(label, "pizza cam on the right: x " + r.xMin.ToString("0") + "-" + r.xMax.ToString("0"));
            else fails += Fail(label, "PIZZA CAM NOT ON THE RIGHT - from x " + r.xMin.ToString("0") + " of " + w);
            float cy = r.center.y;
            if (cy >= map.yMin && cy <= map.yMax)
                Ok(label, "pizza cam centre " + (cy / h * 100f).ToString("0") + "% up - level with the map (" +
                          (map.yMin / h * 100f).ToString("0") + "-" + (map.yMax / h * 100f).ToString("0") + "%)");
            else
                fails += Fail(label, "PIZZA CAM NOT LEVEL WITH THE MAP - centre " + (cy / h * 100f).ToString("0") +
                                     "% up, map " + (map.yMin / h * 100f).ToString("0") + "-" +
                                     (map.yMax / h * 100f).ToString("0") + "%");
            if (r.yMax <= h + 0.5f) Ok(label, "pizza cam on screen: top " + (h - r.yMax).ToString("0") + " px under the edge");
            else fails += Fail(label, "PIZZA CAM OFF THE TOP by " + (r.yMax - h).ToString("0") + " px");
            bool overSpeedo = r.xMax > speedo.Value.xMin && r.xMin < speedo.Value.xMax;
            float speedoClear = r.yMin - speedo.Value.yMax;
            if (!overSpeedo || speedoClear >= -0.5f)
                Ok(label, overSpeedo ? "pizza cam " + speedoClear.ToString("0") + " px above the speedometer"
                                     : "pizza cam beside the speedometer, not over it");
            else fails += Fail(label, "PIZZA CAM OVERLAPS THE SPEEDOMETER by " + (-speedoClear).ToString("0") + " px");
            if (touchPanel != null)
            {
                var brake = PixelRect(cam, touchPanel, "Brake/Level");
                var shift = PixelRect(cam, touchPanel, "Shifter");
                var action = PixelRect(cam, touchPanel, "Action");
                if (action.HasValue)
                {
                    float under = action.Value.yMin - r.yMax;
                    bool across = r.xMax > action.Value.xMin && r.xMin < action.Value.xMax;
                    if (!across || under >= -0.5f)
                        Ok(label, "pizza cam " + under.ToString("0") + " px under the FUEL/ORDER slot");
                    else fails += Fail(label, "PIZZA CAM UNDER THE FUEL/ORDER SLOT by " + (-under).ToString("0") + " px");
                }
                else fails += Fail(label, "the FUEL/ORDER slot is missing - nothing measured");
                if (brake.HasValue && shift.HasValue)
                {
                    float edge = Mathf.Min(brake.Value.xMin, shift.Value.xMin);
                    if (r.xMax <= edge + 0.5f) Ok(label, "pizza cam " + (edge - r.xMax).ToString("0") + " px clear of the pedals");
                    else fails += Fail(label, "PIZZA CAM OVER THE PEDALS by " + (r.xMax - edge).ToString("0") + " px");
                }
            }
            return fails;
        }

        static Transform StandIn(Transform parent, string name, Vector2 anchor, Vector2 pos,
                                 Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go.transform;
        }

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
