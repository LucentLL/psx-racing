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
    /// The layout follows Racing Game 2's cabin arrangement, which the player
    /// asked for by name: a rotary steering WHEEL bottom-left, and a bottom-right
    /// column of brake and throttle with the handbrake and shifter stacked above
    /// them. Every control is analog and multi-touch, so steering, part-throttle
    /// and the handbrake can all be held at once.
    ///
    /// Controls stay hidden until a touch is seen, so desktop play is unaffected
    /// and a WebGL build serves both without a separate configuration.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls Instance { get; private set; }

        public bool forceShow;

        /// <summary>Null when the wheel is untouched — the caller falls through
        /// to its release slew rather than reading an untouched wheel as a
        /// commanded zero.</summary>
        public float? SteerAxis => wheel != null && wheel.Active ? wheel.Axis : null;
        public float Throttle => gasPedal != null ? gasPedal.Amount : 0f;
        public float Brake => brakePedal != null ? brakePedal.Amount : 0f;
        public float HandbrakeAmount => ebrakePedal != null ? ebrakePedal.Amount : 0f;
        public bool Handbrake => HandbrakeAmount > 0.02f;
        /// <summary>
        /// The results screen's CONTINUE, and the only button this panel shows
        /// that is not a control for driving.
        ///
        /// RESET and CAM used to sit in this corner for the whole race. They are
        /// gone at the owner's ask — both are rows in the pause menu, which is
        /// one tap away behind MENU, and two permanent buttons for things you do
        /// once a race is two thumb-sized pieces of a phone screen spent on
        /// nothing. But RESET was also the ONLY way a touch player got off the
        /// results screen, so the escape had to survive the button: this appears
        /// when the race ends and nowhere else.
        /// </summary>
        public bool ContinuePressed => continueBtn != null &&
                                       continueBtn.gameObject.activeSelf &&
                                       continueBtn.PressedThisFrame;
        /// <summary>The contextual button, HELD. Only ever on screen while
        /// something in the world is offering an action — the fuel nozzle is
        /// the only one so far — because a button that does nothing for 99% of
        /// a race is a button that eats a thumb-sized piece of a phone screen
        /// for nothing.</summary>
        public bool ActionHeld => actionBtn != null &&
                                  actionBtn.gameObject.activeSelf && actionBtn.Pressed;
        /// <summary>The same button, TAPPED. Getting out of the car is a press;
        /// working the nozzle is a hold. One control, two verbs, told apart the
        /// way every other control in this game tells them apart.</summary>
        public bool ActionPressed => actionBtn != null &&
                                     actionBtn.gameObject.activeSelf && actionBtn.PressedThisFrame;
        public bool Visible { get; private set; }

        TouchWheel wheel;
        TouchPedal gasPedal, brakePedal, ebrakePedal;
        TouchShifter shifter;
        TouchButton continueBtn, actionBtn;
        Canvas canvas;

        // Palette, carried over from the source's control colours.
        static readonly Color GasGreen = new Color(0.30f, 1f, 0.35f, 1f);
        static readonly Color BrakeRed = new Color(1f, 0.33f, 0.30f, 1f);
        static readonly Color EbrakeAmber = new Color(1f, 0.67f, 0f, 1f);
        static readonly Color ShiftCyan = new Color(0f, 1f, 1f, 1f);

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

        /// <summary>Show or hide the driving panel. Public because the
        /// forecourt takes it away while the player is out of the car — a
        /// steering wheel floating over somebody standing on tarmac is a
        /// control for a seat nobody is in.</summary>
        public void SetVisible(bool v)
        {
            Visible = v;
            if (canvas != null) canvas.enabled = v;
        }

        /// <summary>Let the car drive the controls back, so a keyboard or gamepad
        /// player sees the wheel turn and the pedals move.</summary>
        public void ReflectState(float steerAxis, float throttle, float brake, int gear)
        {
            if (wheel != null) wheel.SetVisualAxis(steerAxis);
            if (gasPedal != null) gasPedal.SetVisualAmount(throttle);
            if (brakePedal != null) brakePedal.SetVisualAmount(brake);
            if (shifter != null) shifter.SetGear(gear);
        }

        public void BindShift(System.Action<int> onShift)
        {
            if (shifter != null) shifter.Shifted += dir => onShift(dir);
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

        // ---- sprite generation --------------------------------------------
        // All of it lives in TouchArt now. It was inline here while the panel
        // was three grey slabs; it is nine pieces of drawn hardware now, and
        // keeping the geometry next to the CSS and SVG it is copied from beats
        // keeping it next to the layout code that positions it.
        static Sprite Circle() => TouchArt.Circle();
        static Sprite Rounded() => TouchArt.Rounded();
        static Sprite Wheel() => TouchArt.Wheel();
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

            BuildWheel(canvasGO.transform);
            BuildPedals(canvasGO.transform, font);

            // The top-right corner is EMPTY while driving now. It carried CAM
            // and RESET permanently; both are pause-menu rows and MENU is one
            // tap away, and the corner they were sitting in is the corner a
            // phone player's thumb crosses to reach the shifter.
            //
            // The contextual pair that remain both appear only when something
            // is offering them: FUEL when the nozzle is in reach, CONTINUE when
            // the race is over. Amber rather than black so each reads as
            // something that has just appeared rather than as a control that was
            // always there and is only now being noticed.
            actionBtn = MakeButton(canvasGO.transform, "Action", "FUEL", font,
                                   new Vector2(1f, 1f), new Vector2(-30f, -30f),
                                   new Vector2(252f, 74f),
                                   new Color(0.55f, 0.36f, 0.02f, 0.82f), 24);
            actionBtn.gameObject.SetActive(false);

            // Bottom centre, wide, and clear of the wheel and the pedals: a
            // results screen is the one moment nothing else on this panel does
            // anything, so the button that leaves it can have the middle of the
            // screen and be impossible to miss.
            continueBtn = MakeButton(canvasGO.transform, "Continue", "CONTINUE", font,
                                     new Vector2(0.5f, 0f), new Vector2(0f, 96f),
                                     new Vector2(340f, 78f),
                                     new Color(0.16f, 0.42f, 0.24f, 0.92f), 26);
            continueBtn.gameObject.SetActive(false);
        }

        /// <summary>Show or hide the results-screen CONTINUE. Driven by the HUD,
        /// which is the component that already ticks every frame and already
        /// knows what state the race is in.</summary>
        public void SetContinue(bool show)
        {
            if (continueBtn != null && continueBtn.gameObject.activeSelf != show)
                continueBtn.gameObject.SetActive(show);
        }
        /// <summary>Show or hide the contextual button, and say what it does.
        /// Driven from the HUD, which is the one component that already ticks
        /// every frame and already knows what the world is offering.</summary>
        public void SetAction(bool show, string label = null)
        {
            if (actionBtn == null) return;
            if (show && !string.IsNullOrEmpty(label))
            {
                var t = actionBtn.GetComponentInChildren<Text>();
                if (t != null && t.text != label) t.text = label;
            }
            if (actionBtn.gameObject.activeSelf != show)
                actionBtn.gameObject.SetActive(show);
        }

        void BuildWheel(Transform parent)
        {
            // The hit area is the full square, corners included — the source
            // does the same, and it makes the wheel far easier to catch.
            var zone = new GameObject("SteerWheel");
            zone.transform.SetParent(parent, false);
            var hit = zone.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);   // invisible but raycastable
            var zoneRT = hit.rectTransform;
            zoneRT.anchorMin = zoneRT.anchorMax = new Vector2(0f, 0f);
            zoneRT.pivot = new Vector2(0f, 0f);
            zoneRT.anchoredPosition = new Vector2(18f, 18f);
            zoneRT.sizeDelta = new Vector2(300f, 300f);
            WheelInset = zoneRT.anchoredPosition.x + zoneRT.sizeDelta.x;
            wheel = zone.AddComponent<TouchWheel>();

            var rimGO = new GameObject("Rim");
            rimGO.transform.SetParent(zone.transform, false);
            var rimImg = rimGO.AddComponent<Image>();
            rimImg.sprite = Wheel();
            rimImg.raycastTarget = false;
            var rimRT = rimImg.rectTransform;
            rimRT.anchorMin = rimRT.anchorMax = new Vector2(0.5f, 0.5f);
            rimRT.pivot = new Vector2(0.5f, 0.5f);
            rimRT.anchoredPosition = Vector2.zero;
            rimRT.sizeDelta = new Vector2(300f, 300f);
            wheel.SetRim(rimRT);
        }

        // ------------------------------------------------------------------
        //  Pedals, handbrake and shifter
        // ------------------------------------------------------------------
        /// <summary>
        /// The source's control bar is 45 x 150 CSS pixels, and every piece of
        /// hardware inside it is dimensioned against that. Scaling the whole
        /// design by ONE factor is what keeps it the same control at a
        /// finger-sized scale: parts drawn to their own numbers dropped into a
        /// bar of a different shape give you a different pedal that merely has
        /// the same parts, which is what the first version was.
        /// </summary>
        const float BarScale = 1.8f;

        /// <summary>How far the wheel's box reaches from the LEFT edge, and
        /// where the pedal column starts measured from the RIGHT — both in
        /// canvas units on this panel's scaler.
        ///
        /// The instrument cluster lives on its own canvas with the SAME scaler
        /// settings, and needs somewhere along this edge to put two dials that
        /// is not on top of either control. It used to guess with a fraction of
        /// the frame width, and a fraction that clears both is a different
        /// fraction every time this panel is retuned — it was wrong within one
        /// build of the last two changes. Reported, not guessed.
        /// </summary>
        public static float WheelInset { get; private set; } = 318f;
        public static float PedalsInset { get; private set; } = 306f;
        static float Px(float cssPx) => cssPx * BarScale;

        enum PedalKind { Gas, Brake, Handbrake }

        void BuildPedals(Transform parent, Font font)
        {
            var bar = new Vector2(Px(45f), Px(150f));
            var stack = new Vector2(Px(45f), Px(80f));
            // Outboard edge of the brake, which is the leftmost thing in this
            // column and therefore what the cluster has to clear.
            const float gasInset = 130f, brakeInset = 225f;
            PedalsInset = brakeInset + bar.x;
            // Real-cabin order: brake outboard, throttle inboard toward the
            // wheel, handbrake and shifter stacked above where a hand already
            // is. The source stacks all four in one column bottom-right, which
            // comes to 500 px tall — fine in a portrait browser, taller than a
            // landscape phone has to give.
            float stackY = 26f + bar.y + 20f;
            brakePedal = MakePedal(parent, PedalKind.Brake, font, new Vector2(-brakeInset, 26f), bar);
            gasPedal = MakePedal(parent, PedalKind.Gas, font, new Vector2(-gasInset, 26f), bar);
            ebrakePedal = MakePedal(parent, PedalKind.Handbrake, font, new Vector2(-gasInset, stackY), stack);
            BuildShifter(parent, font, new Vector2(-brakeInset, stackY), stack);
        }

        /// <summary>
        /// One control bar, assembled the way the source's markup is: a rail of
        /// ticks down each edge, then either the pedal linkage (mount, arm, pad)
        /// or the handbrake lever, then the level pip on top.
        ///
        /// The bar itself is TRANSPARENT and so is the fill, exactly as in the
        /// CSS. What this replaces was a tinted slab with a plain rounded
        /// rectangle sliding around on it — every control the same shape in a
        /// different colour, which is why the panel read as programmer art next
        /// to the browser it was copied from.
        /// </summary>
        TouchPedal MakePedal(Transform parent, PedalKind kind, Font font, Vector2 pos, Vector2 size)
        {
            bool isLever = kind == PedalKind.Handbrake;
            Color tint = kind == PedalKind.Gas ? GasGreen
                       : kind == PedalKind.Brake ? BrakeRed : EbrakeAmber;

            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);      // invisible, still raycastable
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // A rail of ticks down BOTH edges, every 12.5%. One rail reads as
            // decoration; two read as a scale, which is the only reason they are
            // there — and with the bar transparent they are also what marks out
            // where the control is.
            for (int i = 1; i <= 7; i++)
                foreach (float side in new[] { -1f, 1f })
                {
                    var tick = new GameObject("Tick");
                    tick.transform.SetParent(go.transform, false);
                    var ti = tick.AddComponent<Image>();
                    ti.color = new Color(0.4f, 0.4f, 0.4f, 0.55f);
                    ti.raycastTarget = false;
                    var trt = ti.rectTransform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
                    trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.anchoredPosition = new Vector2(
                        side * (size.x * 0.5f - Px(7f)), -size.y * (i / 8f));
                    trt.sizeDelta = new Vector2(Px(6f), Px(1f));
                }

            RectTransform faceRT = null, armRT = null, leverRT = null;

            if (isLever)
            {
                // On a phone the source hides the e-brake's whole pedal stack
                // and shows only the lever, so the lever IS the control.
                // Pivoted at its base and foreshortened by the pull, in
                // TouchPedal.Redraw.
                var lv = new GameObject("Lever");
                lv.transform.SetParent(go.transform, false);
                var li = lv.AddComponent<Image>();
                li.sprite = TouchArt.Handbrake();
                li.raycastTarget = false;
                leverRT = li.rectTransform;
                leverRT.anchorMin = leverRT.anchorMax = new Vector2(0.5f, 0f);
                leverRT.pivot = new Vector2(0.5f, 0f);
                leverRT.anchoredPosition = Vector2.zero;
                leverRT.sizeDelta = new Vector2(Px(30f), size.y);
            }
            else
            {
                // Mount, arm, pad — all hanging from the TOP of the bar, which
                // is the source's default (its `.inverted` class, applied in the
                // markup). The pad rises toward the mount as it is pressed and
                // the arm shortens to match; TouchPedal drives both off the one
                // amount so they cannot come apart.
                var baseGO = new GameObject("Mount");
                baseGO.transform.SetParent(go.transform, false);
                var bi = baseGO.AddComponent<Image>();
                bi.sprite = TouchArt.PedalBase();
                bi.raycastTarget = false;
                var brt = bi.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
                brt.pivot = new Vector2(0.5f, 1f);
                brt.anchoredPosition = new Vector2(0f, -Px(4f));
                brt.sizeDelta = new Vector2(Px(36f), Px(14f));

                var armGO = new GameObject("Arm");
                armGO.transform.SetParent(go.transform, false);
                var ai = armGO.AddComponent<Image>();
                ai.sprite = TouchArt.PedalArm();
                ai.raycastTarget = false;
                armRT = ai.rectTransform;
                armRT.anchorMin = armRT.anchorMax = new Vector2(0.5f, 1f);
                armRT.pivot = new Vector2(0.5f, 1f);          // shortens from the mount
                armRT.anchoredPosition = new Vector2(0f, -Px(18f));
                armRT.sizeDelta = new Vector2(Px(5f), Px(60f));

                var faceGO = new GameObject("Pad");
                faceGO.transform.SetParent(go.transform, false);
                var fi = faceGO.AddComponent<Image>();
                bool gas = kind == PedalKind.Gas;
                fi.sprite = gas ? TouchArt.GasFace() : TouchArt.BrakeFace();
                fi.raycastTarget = false;
                faceRT = fi.rectTransform;
                faceRT.anchorMin = faceRT.anchorMax = new Vector2(0.5f, 1f);
                faceRT.pivot = new Vector2(0.5f, 1f);
                faceRT.anchoredPosition = new Vector2(0f, -Px(78f));
                faceRT.sizeDelta = gas ? new Vector2(Px(26f), Px(62f))
                                       : new Vector2(Px(30f), Px(38f));
            }

            // The level pip. The source calls this "vital" for reading how far
            // the control is pressed, and it is the one part of the bar that is
            // bright.
            var thumbGO = new GameObject("Level");
            thumbGO.transform.SetParent(go.transform, false);
            var thumbImg = thumbGO.AddComponent<Image>();
            thumbImg.sprite = Rounded();
            thumbImg.type = Image.Type.Sliced;
            // THE COLOUR IS THE LABEL NOW. The caption under each pedal is gone
            // at the owner's ask, and it was carrying the only thing that told
            // the three controls apart: the bar itself is transparent, so
            // without a word under it GAS, BRK and E-BRK were three identical
            // white slides. Tinting the thumb keeps the distinction and spends
            // no text on it — green under the right thumb is the throttle
            // wherever it happens to be.
            thumbImg.color = new Color(tint.r, tint.g, tint.b, 0.95f);
            thumbImg.raycastTarget = false;
            var thumbRT = thumbImg.rectTransform;
            thumbRT.anchorMin = thumbRT.anchorMax = new Vector2(0.5f, 0f);
            thumbRT.pivot = new Vector2(0.5f, 0.5f);
            thumbRT.anchoredPosition = Vector2.zero;
            thumbRT.sizeDelta = new Vector2(size.x + Px(6f), Px(5f));

            var pedal = go.AddComponent<TouchPedal>();
            // Set BEFORE SetParts, which redraws off them.
            pedal.topMounted = isLever;
            pedal.faceTravel = Px(28f);
            pedal.SetParts(null, thumbRT, faceRT, armRT, leverRT);
            return pedal;
        }

        /// <summary>
        /// The shifter: a knob on a gate, rather than a slab with a number on
        /// it. A circular puck lit from the upper left, a recessed dial carrying
        /// the gear, and the cyan centre line that gives the throw a datum.
        /// </summary>
        void BuildShifter(Transform parent, Font font, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Shifter");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var line = new GameObject("Gate");
            line.transform.SetParent(go.transform, false);
            var li = line.AddComponent<Image>();
            li.color = new Color(ShiftCyan.r, ShiftCyan.g, ShiftCyan.b, 0.55f);
            li.raycastTarget = false;
            var lrt = li.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(size.x - Px(8f), Px(2f));

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(go.transform, false);
            var knobImg = knobGO.AddComponent<Image>();
            knobImg.sprite = TouchArt.ShiftKnob();
            knobImg.raycastTarget = false;
            var krt = knobImg.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0.5f, 0.5f);
            krt.pivot = new Vector2(0.5f, 0.5f);
            krt.sizeDelta = new Vector2(Px(44f), Px(44f));

            var recessGO = new GameObject("Recess");
            recessGO.transform.SetParent(knobGO.transform, false);
            var ri = recessGO.AddComponent<Image>();
            ri.sprite = TouchArt.ShiftRecess();
            ri.raycastTarget = false;
            var rrt = ri.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(Px(30f), Px(30f));

            var gearText = MakeLabel(recessGO.transform, "1", font,
                                     Mathf.RoundToInt(Px(17f)), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, 1f);

            // No SHIFT caption either. The ball still carries the gear NUMBER,
            // which is information rather than a name — a driver needs to know
            // they are in third, not to be told that a gear knob is a gear knob.

            shifter = go.AddComponent<TouchShifter>();
            shifter.SetParts(krt, gearText);
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
            MakeLabel(go.transform, label, font, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, 1f);
            return go.AddComponent<TouchButton>();
        }

        static Text MakeLabel(Transform parent, string text, Font font, int size,
                              Vector2 anchor, Vector2 pos, float alpha)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            // The caption. This assignment was MISSING: every label on the touch
            // panel took its text as a parameter and threw it away, so GAS,
            // BRAKE, E-BRK, CAM and RESET have been blank slabs since the panel
            // was written — the shifter's gear number only showed because
            // TouchShifter writes it again every frame. It is also why the six
            // camera views were reported as having no control: the button that
            // cycles them was an unlabelled rectangle. A previous pass darkened
            // these buttons and made the type solid to fix "two blank grey
            // slabs", which could not have worked on text that was not there.
            t.text = text;
            t.color = new Color(1f, 1f, 1f, alpha);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(240f, 40f);
            return t;
        }
    }
}
