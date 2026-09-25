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

        /// <summary>Clearance from whatever the panel stands beside or above in
        /// the bottom-right: the speedometer's bezel, and on a phone the pedal
        /// column, where a gap is what keeps a thumb on the throttle from
        /// covering the picture.</summary>
        const float AboveWheelGap = 14f;

        PizzaCargo cargo;
        Camera cam;
        RenderTexture rt;
        RawImage view;
        Text caption;
        RectTransform panelRT;
        bool builtTouch;
        /// <summary>The right-corner height this panel was last placed against,
        /// so a cluster rebuild that moves the dials moves this too.</summary>
        float builtCornerTop = -1f;

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
            // TIGHT ON THE SEAT. Two notes from the owner, and they are the same
            // note: "no need to show the floorboard" and "no black background,
            // just the car seat and pizzas". The wide framing was mostly void
            // with a bench in the middle of it, so this comes in close enough
            // that the pan and the backrest run off every edge and there IS no
            // background left to be black.
            eye = origin + new Vector3(0.40f, 0.40f, 0.62f);
            look = origin + new Vector3(0f, 0.03f, -0.02f);
            fov = 52f;
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

        /// <summary>
        /// The panel alone — no cargo, no lens, a flat stand-in picture — for
        /// the editor's HUD preview, which measures where it lands against the
        /// dials and the pedals. Same Place() as the real one, so the check is
        /// of the player's layout.
        /// </summary>
        public static PizzaCam SpawnPanelForPreview()
        {
            var go = new GameObject("PizzaCam");
            var pc = go.AddComponent<PizzaCam>();
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.85f, 0.55f, 0.20f, 1f));
            tex.Apply();
            pc.BuildPanel(tex);
            pc.caption.text = "PIZZA CAM  x3";
            return pc;
        }

        /// <summary>For the preview: re-place against whatever the cluster
        /// and the touch panel have published since.</summary>
        public void PlaceNow() => Place();

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

            // ARGB32, not Default: this buffer's ALPHA is the whole point. The
            // camera clears it to nothing and only the seat and the boxes write
            // to it, so the panel is a cutout of cargo over the game rather than
            // a black television in the corner of the screen.
            rt = new RenderTexture(ViewW, ViewH, 24, RenderTextureFormat.ARGB32)
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
            // Six metres. The world is four kilometres up, so anything under a
            // kilometre is a free guarantee that none of it is ever in frame —
            // and the first value, 2.2, was clipping the cargo's own cabin
            // backdrop, which sits two metres out. A far plane tight enough to
            // cut the set is not a safety margin, it is a bug.
            cam.farClipPlane = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // TRANSPARENT. Alpha zero, so everything the seat does not cover is
            // the game behind it.
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.targetTexture = rt;
            // Below the main camera's depth so it renders first and its texture
            // is ready when the UI draws.
            cam.depth = -10f;

            BuildPanel(rt);
        }

        void BuildPanel(Texture picture)
        {
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
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(1f, 0f);
            panelRT.pivot = new Vector2(1f, 0f);
            panelRT.sizeDelta = new Vector2(PanelW, PanelH);
            // No panel behind the picture either — the frame exists only to give
            // the caption and the view something to lay out against.
            var frame = panel.AddComponent<Image>();
            frame.color = new Color(0f, 0f, 0f, 0f);
            frame.raycastTarget = false;

            var viewGO = new GameObject("View", typeof(RectTransform));
            viewGO.transform.SetParent(panel.transform, false);
            view = viewGO.AddComponent<RawImage>();
            view.texture = picture;
            view.raycastTarget = false;
            var vrt = view.rectTransform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = new Vector2(0f, -18f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var capGO = new GameObject("Caption", typeof(RectTransform));
            capGO.transform.SetParent(panel.transform, false);
            caption = capGO.AddComponent<Text>();
            caption.font = font;
            caption.fontSize = 15;
            // Right-aligned: the panel stands on the right edge, and a caption
            // that starts at the picture's far side reads as floating.
            caption.alignment = TextAnchor.MiddleRight;
            caption.raycastTarget = false;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            // The caption now sits over the world rather than over a black
            // panel, so it carries its own shadow — the same one every readout
            // on the race HUD uses, for the same reason.
            var capShadow = capGO.AddComponent<UnityEngine.UI.Shadow>();
            capShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            capShadow.effectDistance = new Vector2(1f, -1f);
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-7f, -2f);
            crt.sizeDelta = new Vector2(-10f, 18f);

            Place();
        }

        /// <summary>
        /// Where the panel sits: on the RIGHT edge, opposite the race map on
        /// the left — "opposite of the race map" (owner, 2026-09-25). It used
        /// to stand in the bottom-left corner under the map, which stacked the
        /// two things a driver glances at on one side and left the other empty.
        ///
        /// ON A PHONE, AT THE TOP, like the map ("On mobile, pizza cam and race
        /// map should be top of screen", same day): under the slot the FUEL /
        /// ORDER button appears in, and in off the pedal column, whose
        /// handbrake reaches most of the way up that edge.
        ///
        /// Level with the map unless that would put it on whatever is already
        /// in the bottom-right: the speedometer on a PC, the speedometer beside
        /// the pedals on a phone (where the panel also steps in off the pedal
        /// column, which runs up that edge), or the cockpit's binnacle. Then it
        /// stands just clear above it.
        ///
        /// Every box is asked for rather than guessed — TouchControls.
        /// PedalsInset and GaugeCluster.RightCornerTop are published for
        /// exactly this, since a fraction of the screen that clears a dial is a
        /// different fraction every time it is retuned. All three canvases
        /// scale off the same 1280x720 reference, so the numbers are directly
        /// comparable.
        /// </summary>
        void Place()
        {
            if (panelRT == null) return;
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            builtTouch = touch;
            builtCornerTop = GaugeCluster.RightCornerTop;
            float frameH = builtFrameH = FrameH();
            if (mapCentreYFrac < 0f)
            {
                var hud = FindAnyObjectByType<RaceHUD>();
                mapCentreYFrac = hud != null ? hud.mapCentreYFrac : 0.60f;
            }
            float levelWithMap = frameH * mapCentreYFrac - PanelH * 0.5f;
            float underAction = frameH - TouchControls.ActionTopInset - TouchControls.ActionH
                              - AboveWheelGap - PanelH;
            float y = Mathf.Max(touch ? underAction : levelWithMap, builtCornerTop + AboveWheelGap);
            // Never off the top: a short landscape window gives up "level with
            // the map" before it gives up the picture.
            y = Mathf.Min(y, frameH - PanelH - 18f);
            float x = touch ? TouchControls.PedalsInset + AboveWheelGap : 18f;
            panelRT.anchoredPosition = new Vector2(-x, y);
        }

        /// <summary>RaceHUD's map height, read once; negative until then.</summary>
        float mapCentreYFrac = -1f;
        /// <summary>The canvas height this was placed on. The scaler settles
        /// after Build, and a window can be resized mid-race.</summary>
        float builtFrameH = -1f;

        float FrameH()
        {
            var canvasRT = panelRT != null ? panelRT.parent as RectTransform : null;
            return canvasRT != null && canvasRT.rect.height > 32f ? canvasRT.rect.height : 720f;
        }

        void LateUpdate()
        {
            // The panel moves if the player plugs in a pad mid-race and the
            // touch controls hide themselves — and with them, on the same frame,
            // the dials move into the corner this is standing in. The cluster
            // rebuilds on its own schedule, so the corner is re-read rather than
            // assumed to have settled by the time the touch flag flips.
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            if (touch != builtTouch
                || !Mathf.Approximately(GaugeCluster.RightCornerTop, builtCornerTop)
                || !Mathf.Approximately(FrameH(), builtFrameH)) Place();

            if (caption == null || cargo == null) return;
            float c = cargo.Condition;
            // In a replay the caption reads the RECORDED load, like the picture
            // does, not the load as it lies at the flag.
            if (RaceReplay.Playing && RaceReplay.Instance.TryCargoCondition(out float replayed)) c = replayed;
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
