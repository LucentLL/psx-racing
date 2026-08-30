using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// The Pizza Cam: a little window onto the passenger seat, so the player can
    /// watch the cargo they are being paid for slide, tip and eventually leave
    /// its box.
    ///
    /// It is the whole point of the cargo being a simulation rather than a
    /// counter. A tip that quietly drains because of a number the player cannot
    /// see is a punishment; a box visibly walking toward the footwell on the
    /// approach to a corner is a reason to lift.
    ///
    /// The picture is deliberately TINY and point-filtered — 160 lines wide,
    /// which at PSX output is roughly the resolution of the rest of the game and
    /// costs nothing to render. It is a second camera on four rigidbodies, not a
    /// second view of the world.
    ///
    /// The camera does NOT tilt with the car. It is fixed in the cargo island's
    /// frame, which is to say fixed relative to gravity, so what the player sees
    /// is the SEAT rolling and pitching under the boxes. A camera bolted to the
    /// car would hold the seat still and tilt nothing but a background that
    /// isn't there — the attitude is the information, and this is the framing
    /// that shows it.
    /// </summary>
    public class PizzaCam : MonoBehaviour
    {
        public static PizzaCam Instance { get; private set; }

        /// <summary>Framebuffer for the little view. Small on purpose.</summary>
        const int ViewW = 160, ViewH = 108;

        /// <summary>Panel size in canvas units on the 1280x720 reference the
        /// touch panel and the cluster both use — a canvas unit has to mean the
        /// same thing here as it does there, because this thing is placed
        /// against the steering wheel's reported box.</summary>
        const float PanelW = 236f, PanelH = 159f;

        /// <summary>Clearance above the wheel. The wheel's box is 300 units tall
        /// sitting 18 off the bottom; the owner asked for the cam ABOVE it on
        /// mobile, and a gap is what keeps a thumb resting on the rim from
        /// covering the picture.</summary>
        const float AboveWheelGap = 14f;

        PizzaCargo cargo;
        Camera cam;
        RenderTexture rt;
        RawImage view;
        Text caption;
        RectTransform panelRT;
        bool builtTouch;

        /// <summary>
        /// Where the lens goes, in the cargo island's frame.
        ///
        /// ONE definition, because the headless harness shoots the same three
        /// moments and its pictures are only worth anything if they are the
        /// player's picture. The first framing was tight on the seat and the
        /// verification shots caught it immediately: a crash throws the boxes
        /// FORWARD into the footwell, which is between the seat and the camera,
        /// so the one moment the cam exists for happened off-screen. Back, up,
        /// and aimed at a point between the two.
        /// </summary>
        public static void Framing(Vector3 origin, out Vector3 eye, out Vector3 look, out float fov)
        {
            eye = origin + new Vector3(0.62f, 0.60f, 1.06f);
            look = origin + new Vector3(0f, -0.06f, 0.22f);
            fov = 50f;
        }

        public static PizzaCam Spawn(PizzaCargo forCargo)
        {
            if (forCargo == null) return null;
            var go = new GameObject("PizzaCam");
            var pc = go.AddComponent<PizzaCam>();
            pc.cargo = forCargo;
            pc.Build();
            return pc;
        }

        void Awake() { if (Instance == null) Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        }

        void Build()
        {
            var seat = cargo.Tray;
            if (seat == null) return;
            Vector3 origin = cargo.transform.position;

            rt = new RenderTexture(ViewW, ViewH, 24, RenderTextureFormat.Default)
            {
                filterMode = FilterMode.Point,
                antiAliasing = 1,
                name = "PizzaCamRT",
            };
            rt.Create();

            var camGO = new GameObject("PizzaLens");
            camGO.transform.SetParent(transform, false);
            cam = camGO.AddComponent<Camera>();
            Framing(origin, out Vector3 eye, out Vector3 look, out float fov);
            camGO.transform.SetPositionAndRotation(
                eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            // Two metres. The world is four kilometres up and this is the
            // cheapest possible guarantee that none of it is ever in frame.
            cam.farClipPlane = 2.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.09f);
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.targetTexture = rt;
            // Below the main camera's depth so it renders first and its texture
            // is ready when the UI draws.
            cam.depth = -10f;

            var canvasGO = new GameObject("PizzaCamCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the cluster (90), under the controls (100): the cam is an
            // instrument, and nothing should ever sit between a thumb and the
            // wheel.
            canvas.sortingOrder = 95;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGO.transform, false);
            panelRT = (RectTransform)panel.transform;
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 0f);
            panelRT.pivot = new Vector2(0f, 0f);
            panelRT.sizeDelta = new Vector2(PanelW, PanelH);
            var frame = panel.AddComponent<Image>();
            frame.color = new Color(0f, 0f, 0f, 0.72f);
            frame.raycastTarget = false;

            var viewGO = new GameObject("View", typeof(RectTransform));
            viewGO.transform.SetParent(panel.transform, false);
            view = viewGO.AddComponent<RawImage>();
            view.texture = rt;
            view.raycastTarget = false;
            var vrt = view.rectTransform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(3f, 3f);
            vrt.offsetMax = new Vector2(-3f, -20f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var capGO = new GameObject("Caption", typeof(RectTransform));
            capGO.transform.SetParent(panel.transform, false);
            caption = capGO.AddComponent<Text>();
            caption.font = font;
            caption.fontSize = 15;
            caption.alignment = TextAnchor.MiddleLeft;
            caption.raycastTarget = false;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(7f, -2f);
            crt.sizeDelta = new Vector2(-10f, 18f);

            Place();
        }

        /// <summary>
        /// Where the panel sits: above the steering wheel when the player has
        /// one, bottom-left when they do not.
        ///
        /// The wheel's box is asked for rather than guessed —
        /// TouchControls.WheelInset is published for exactly this, because a
        /// fraction of the screen that clears the wheel is a different fraction
        /// every time the panel is retuned, and the cluster has already been
        /// caught out by that once.
        /// </summary>
        void Place()
        {
            if (panelRT == null) return;
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            builtTouch = touch;
            panelRT.anchoredPosition = touch
                ? new Vector2(18f, 18f + 300f + AboveWheelGap)
                : new Vector2(18f, 18f);
        }

        void LateUpdate()
        {
            // The panel moves if the player plugs in a pad mid-race and the
            // touch controls hide themselves; nothing else changes.
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            if (touch != builtTouch) Place();

            if (caption == null || cargo == null) return;
            float c = cargo.Condition;
            string label = cargo.BoxCount > 1
                ? "PIZZA CAM  x" + cargo.BoxCount
                : "PIZZA CAM";
            if (caption.text != label) caption.text = label;
            // Amber as it degrades, red once the customer would refuse it. The
            // caption is the one part of this panel that can be read at a glance
            // without looking away from the road.
            var want = c >= LifeSim.LifeRules.PizzaPerfectCondition
                     ? new Color(0.78f, 0.80f, 0.84f)
                     : c > LifeSim.LifeRules.PizzaRuinedCondition
                         ? new Color(1f, 0.78f, 0.25f)
                         : new Color(1f, 0.35f, 0.30f);
            if (caption.color != want) caption.color = want;
        }
    }
}
