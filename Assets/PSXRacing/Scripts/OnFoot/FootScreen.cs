using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Everything the garage prints over the picture: the crosshair, the name
    /// of whatever you are standing in front of, what pressing USE would do,
    /// the wallet, and the controls.
    ///
    /// On its OWN overlay canvas at device resolution rather than in the
    /// 240-line framebuffer with the room. That is the project's existing split
    /// and this screen is squarely on the readable side of it: the garage is
    /// where the player reads a car's condition and a parts list, and eight
    /// pixels of dynamic-font glyph is a grey smudge whatever you upscale it
    /// with. The ROOM dithers and crawls; the words do not.
    /// </summary>
    public class FootScreen : MonoBehaviour
    {
        public FootInteractor interactor;
        public FirstPersonWalk walker;
        /// <summary>Where the player is standing, for the header. The same kit
        /// serves the garage and the forecourt, and a line reading GARAGE while
        /// the player is stood at a petrol pump is the screen not knowing where
        /// it is.</summary>
        public string place = "GARAGE";
        /// <summary>Print the wallet and the date. The forecourt already has a
        /// race HUD carrying its own information and does not want a second one
        /// arguing with it.</summary>
        public bool showWallet = true;
        /// <summary>Whether the player is on foot at all. The forecourt keeps
        /// this screen alive between visits — its canvas is built once — and
        /// blanks it rather than rebuilding a canvas every time somebody opens
        /// a car door.</summary>
        public bool show = true;
        /// <summary>The thumb panel, when this device has one. Resolved once —
        /// asking the scene graph for it every frame to decide what a hint line
        /// says is a search per frame for an answer that cannot change.</summary>
        public FootTouchPanel panel;

        Text titleText, detailText, actionText, action2Text, headerText, hintText, toastText;
        Image crosshair;

        string toast;
        float toastUntil;

        static readonly Color Accent = new Color(1f, 0.80f, 0.25f);
        static readonly Color Dim = new Color(0.74f, 0.76f, 0.86f);

        LifeState S => LifeSimManager.State;

        void Start()
        {
            if (panel == null) panel = FindAnyObjectByType<FootTouchPanel>();
            Build();
        }

        bool Thumbs => panel != null && panel.Visible;

        public void Toast(string message)
        {
            toast = message;
            toastUntil = Time.unscaledTime + 2.6f;
        }

        void Build()
        {
            var canvasGO = new GameObject("GarageCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // A dot, not a reticle. There is nothing to aim at in here — it is
            // there so the player knows the middle of the screen is what the
            // prompt is talking about.
            var chGO = new GameObject("Crosshair");
            chGO.transform.SetParent(canvasGO.transform, false);
            crosshair = chGO.AddComponent<Image>();
            crosshair.color = new Color(1f, 1f, 1f, 0.45f);
            crosshair.raycastTarget = false;
            var chRT = crosshair.rectTransform;
            chRT.anchorMin = chRT.anchorMax = new Vector2(0.5f, 0.5f);
            chRT.sizeDelta = new Vector2(4f, 4f);

            headerText = Label(canvasGO.transform, font, 22, new Vector2(0f, 1f),
                               new Vector2(28f, -26f), TextAnchor.UpperLeft, Accent, 760f, 60f);

            titleText = Label(canvasGO.transform, font, 30, new Vector2(0.5f, 0f),
                              new Vector2(0f, 196f), TextAnchor.LowerCenter, Color.white, 980f, 44f);
            titleText.fontStyle = FontStyle.Bold;
            detailText = Label(canvasGO.transform, font, 19, new Vector2(0.5f, 0f),
                               new Vector2(0f, 166f), TextAnchor.LowerCenter, Dim, 980f, 30f);
            actionText = Label(canvasGO.transform, font, 23, new Vector2(0.5f, 0f),
                               new Vector2(0f, 128f), TextAnchor.LowerCenter, Accent, 980f, 34f);
            actionText.fontStyle = FontStyle.Bold;
            // The second verb sits under the first and a size down, because it
            // is the same offer made twice and the reading order is what says
            // which one is the obvious thing to press.
            action2Text = Label(canvasGO.transform, font, 20, new Vector2(0.5f, 0f),
                                new Vector2(0f, 100f), TextAnchor.LowerCenter,
                                new Color(0.78f, 0.86f, 1f), 980f, 30f);
            action2Text.fontStyle = FontStyle.Bold;

            toastText = Label(canvasGO.transform, font, 24, new Vector2(0.5f, 1f),
                              new Vector2(0f, -34f), TextAnchor.UpperCenter,
                              new Color(0.55f, 1f, 0.62f), 980f, 34f);

            hintText = Label(canvasGO.transform, font, 17, new Vector2(0f, 0f),
                             new Vector2(28f, 24f), TextAnchor.LowerLeft, Dim, 900f, 52f);
        }

        static Text Label(Transform parent, Font font, int size, Vector2 anchor, Vector2 pos,
                          TextAnchor align, Color color, float width, float height)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.95f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, height);
            return t;
        }

        // Every line on this screen is a string built by concatenation, and all
        // five of them change about once a minute. Rebuilt per frame they are
        // five allocations and five text-mesh rebuilds every frame — the exact
        // cost the race HUD change-gates itself to avoid, on a screen where the
        // player is standing still and looking at type.
        FootTarget lastIt;
        string lastCtrl, lastHint;
        int lastMoney = int.MinValue, lastDay = int.MinValue;
        bool lastCaptured, lastPad;

        /// <summary>Force the prompt to be re-read. The world edits an
        /// interactable's wording IN PLACE when the player takes the keys to a
        /// different car, so identity is not enough to notice it.</summary>
        public void Invalidate() => lastIt = null;

        void Update()
        {
            if (!show)
            {
                Set(titleText, ""); Set(detailText, ""); Set(actionText, "");
                Set(action2Text, "");
                Set(headerText, ""); Set(hintText, ""); Set(toastText, "");
                if (crosshair != null && crosshair.enabled) crosshair.enabled = false;
                lastIt = null; lastHint = null;
                return;
            }
            if (crosshair != null && !crosshair.enabled) crosshair.enabled = true;

            var it = interactor != null ? interactor.Current : null;
            string ctrl = UseControlName();

            if (it != lastIt || ctrl != lastCtrl)
            {
                lastIt = it;
                lastCtrl = ctrl;
                Set(titleText, it != null ? it.title : "");
                Set(detailText, it != null ? it.detail : "");
                Set(actionText, it != null && !string.IsNullOrEmpty(it.action)
                                ? ctrl + "  " + it.action : "");
                Set(action2Text, it != null && !string.IsNullOrEmpty(it.action2)
                                 ? (Use2ControlName() + "  " + it.action2).Trim() : "");
                if (crosshair != null)
                    crosshair.color = it != null ? new Color(1f, 0.82f, 0.3f, 0.9f)
                                                 : new Color(1f, 1f, 1f, 0.4f);
            }

            if (!showWallet) Set(headerText, "");
            else if (S.money != lastMoney || S.day != lastDay)
            {
                lastMoney = S.money;
                lastDay = S.day;
                Set(headerText, place + "   ·   " + MenuKit.Money(S.money) + "   ·   " +
                                LifeRules.DateLabel(S.day).ToUpperInvariant());
            }

            // The pad is in the gate as well as the cursor: the hint names the
            // controls the player HAS, and a pad plugged in halfway through
            // changes every one of them.
            bool captured = walker != null && walker.MouseCaptured;
            bool pad = Gamepad.current != null;
            if (captured != lastCaptured || pad != lastPad || lastHint == null)
            {
                lastCaptured = captured;
                lastPad = pad;
                lastHint = HintLines();
                Set(hintText, lastHint);
            }

            Set(toastText, Time.unscaledTime < toastUntil ? toast : "");
        }

        string HintLines()
        {
            if (Thumbs) return "LEFT THUMB WALKS  ·  RIGHT THUMB LOOKS  ·  USE BUTTON ACTS";

            if (Gamepad.current != null)
                return "LEFT STICK MOVES   ·   RIGHT STICK LOOKS   ·   A / CROSS USES";

            string line = "WASD / ARROWS MOVE   ·   MOUSE LOOKS   ·   F OR ENTER USES";
            if (walker != null && !walker.MouseCaptured)
                line = "CLICK TO LOOK AROUND\n" + line + "   ·   ESC FREES THE MOUSE";
            return line;
        }

        string UseControlName()
        {
            if (Thumbs) return "[USE]";
            return Gamepad.current != null ? "[A / CROSS]" : "[F]";
        }

        /// <summary>Empty on a touchscreen: the second thumb button is
        /// labelled with the verb itself, so a bracket in front of the same
        /// words would be naming a key that device does not have.</summary>
        string Use2ControlName()
        {
            if (Thumbs) return "";
            return Gamepad.current != null ? "[X / SQUARE]" : "[E]";
        }

        static void Set(Text field, string value)
        {
            if (field != null && field.text != value) field.text = value;
        }
    }
}
