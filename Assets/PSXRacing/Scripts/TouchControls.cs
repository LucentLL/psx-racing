using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace PSXRacing
{
    /// <summary>
    /// Builds and owns the on-screen driving controls for phones and tablets.
    /// The whole UI is generated at runtime on its own overlay canvas at full
    /// device resolution, so the touch targets stay finger-sized even though the
    /// game itself renders at 320x240.
    ///
    /// Controls stay hidden until a touch is seen, so desktop play is unaffected
    /// and a WebGL build serves both without a separate configuration.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls Instance { get; private set; }

        public bool forceShow;

        public float Steer => steerPad != null ? steerPad.Steer : 0f;
        public float Throttle => gasBtn != null && gasBtn.Pressed ? 1f : 0f;
        public float Brake => brakeBtn != null && brakeBtn.Pressed ? 1f : 0f;
        public bool Handbrake => hbBtn != null && hbBtn.Pressed;
        public bool RestartPressed => restartBtn != null && restartBtn.PressedThisFrame;
        public bool CameraPressed => camBtn != null && camBtn.PressedThisFrame;
        public bool Visible { get; private set; }

        TouchSteerPad steerPad;
        TouchButton gasBtn, brakeBtn, hbBtn, restartBtn, camBtn;
        Canvas canvas;
        static Sprite circleSprite, roundedSprite;

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            BuildUI();
            SetVisible(forceShow || Application.isMobilePlatform ||
                       SystemInfo.deviceType == DeviceType.Handheld);
        }

        void Update()
        {
            // Reveal on the first real touch — covers mobile browsers that do not
            // report isMobilePlatform until the user interacts.
            if (!Visible && Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.isPressed)
                SetVisible(true);
        }

        void SetVisible(bool v)
        {
            Visible = v;
            if (canvas != null) canvas.enabled = v;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // Project runs the Input System package exclusively (activeInputHandler: 1),
            // so the legacy StandaloneInputModule would fail here.
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ---- sprite generation (no art assets needed) ----------------------
        // Both build a pixel array and upload once. Per-pixel SetPixel would be
        // ~20k interop calls at Awake, which is a visible hitch on a phone.
        static Sprite Circle()
        {
            if (circleSprite != null) return circleSprite;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - (S - 1) * 0.5f) / (S * 0.5f);
                    float dy = (y - (S - 1) * 0.5f) / (S * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - d) * 12f);         // soft edge
                    float ring = Mathf.Clamp01((d - 0.78f) * 10f);   // brighter rim
                    px[y * S + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(a * (0.55f + 0.45f * ring)) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            circleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            return circleSprite;
        }

        static Sprite Rounded()
        {
            if (roundedSprite != null) return roundedSprite;
            const int S = 64, R = 16;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(R - x, x - (S - 1 - R)));
                    float dy = Mathf.Max(0, Mathf.Max(R - y, y - (S - 1 - R)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * S + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01((R - d) * 0.8f) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            roundedSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
                                          100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return roundedSprite;
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("TouchCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // above the PSX display RawImage
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ---- steering pad (left) ----
            var padGO = new GameObject("SteerPad");
            padGO.transform.SetParent(canvasGO.transform, false);
            var padImg = padGO.AddComponent<Image>();
            padImg.sprite = Rounded();
            padImg.type = Image.Type.Sliced;
            padImg.color = new Color(1f, 1f, 1f, 0.10f);
            var padRT = padImg.rectTransform;
            padRT.anchorMin = new Vector2(0f, 0f);
            padRT.anchorMax = new Vector2(0f, 0f);
            padRT.pivot = new Vector2(0f, 0f);
            padRT.anchoredPosition = new Vector2(24f, 24f);
            padRT.sizeDelta = new Vector2(430f, 210f);
            steerPad = padGO.AddComponent<TouchSteerPad>();

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(padGO.transform, false);
            var knobImg = knobGO.AddComponent<Image>();
            knobImg.sprite = Circle();
            knobImg.color = new Color(1f, 1f, 1f, 0.28f);
            knobImg.raycastTarget = false;
            knobImg.rectTransform.sizeDelta = new Vector2(96f, 96f);
            steerPad.SetKnob(knobImg.rectTransform);

            MakeLabel(padGO.transform, "< STEER >", font, 22,
                      new Vector2(0.5f, 0f), new Vector2(0f, 22f), 0.35f);

            // ---- pedals (right) ----
            gasBtn = MakeButton(canvasGO.transform, "Gas", "GAS", font,
                                new Vector2(1f, 0f), new Vector2(-30f, 30f),
                                new Vector2(190f, 190f), new Color(0.45f, 1f, 0.55f, 0.22f), 30);
            brakeBtn = MakeButton(canvasGO.transform, "Brake", "BRAKE", font,
                                  new Vector2(1f, 0f), new Vector2(-236f, 30f),
                                  new Vector2(150f, 150f), new Color(1f, 0.45f, 0.42f, 0.22f), 24);
            hbBtn = MakeButton(canvasGO.transform, "Handbrake", "E-BRAKE", font,
                               new Vector2(1f, 0f), new Vector2(-146f, 226f),
                               new Vector2(150f, 110f), new Color(1f, 0.85f, 0.35f, 0.22f), 20);
            camBtn = MakeButton(canvasGO.transform, "Cam", "CAM", font,
                                new Vector2(1f, 1f), new Vector2(-30f, -30f),
                                new Vector2(110f, 70f), new Color(1f, 1f, 1f, 0.16f), 18);
            restartBtn = MakeButton(canvasGO.transform, "Restart", "RESET", font,
                                    new Vector2(1f, 1f), new Vector2(-152f, -30f),
                                    new Vector2(110f, 70f), new Color(1f, 1f, 1f, 0.16f), 18);
        }

        TouchButton MakeButton(Transform parent, string name, string label, Font font,
                               Vector2 anchor, Vector2 pos, Vector2 size, Color color, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            bool round = Mathf.Abs(size.x - size.y) < 1f;
            img.sprite = round ? Circle() : Rounded();
            if (!round) img.type = Image.Type.Sliced;
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            MakeLabel(go.transform, label, font, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, 0.85f);
            return go.AddComponent<TouchButton>();
        }

        static void MakeLabel(Transform parent, string text, Font font, int size,
                              Vector2 anchor, Vector2 pos, float alpha)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(1f, 1f, 1f, alpha);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(240f, 40f);
        }
    }
}
