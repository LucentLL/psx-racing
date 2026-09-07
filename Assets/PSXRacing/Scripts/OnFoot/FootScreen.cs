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
            // THE PAGER RIDES OUT ON THE NEXT TOAST, and this is now one of the
            // screens that can cause one. LifeRules.lastPage is a static one-shot
            // written by the day rollover and, until the bed, drained by exactly
            // one place — LifeHomeScreen.Toast, whose comment states the
            // contract: "Every path that can roll the day ends in exactly one
            // Toast, so draining it here catches all of them instead of four
            // call sites each remembering to ask." Sleeping at the bed made the
            // walk-in home the first day-rolling path outside that menu, and
            // Blacklist.TickPager is destructive-once: undrained, a call-out
            // headline is either lost to a page reload or turns up days later
            // glued to an unrelated line. Here rather than in the bed, so the
            // next walk-in verb that rolls a day does not have to remember.
            if (!string.IsNullOrEmpty(LifeRules.lastPage))
            {
                message = "PAGER — " + LifeRules.lastPage + "   ·   " + message;
                LifeRules.lastPage = null;
            }
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
        int lastMoney = int.MinValue, lastDay = int.MinValue, lastSlot = int.MinValue;
        bool lastCaptured, lastPad, lastInvert;

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
            // Target-aware on a touchscreen, where it names the BUTTON — so on
            // thumbs the verb is inside ctrl and a verb rewritten in place
            // repaints through this gate on its own. On a keyboard or pad it
            // is not, and an in-place rewording still needs Invalidate(), which
            // every RefreshLabels calls.
            string ctrl = UseControlName(it);

            if (it != lastIt || ctrl != lastCtrl)
            {
                lastIt = it;
                lastCtrl = ctrl;
                Set(titleText, it != null ? it.title : "");
                Set(detailText, it != null ? it.detail : "");
                Set(actionText, it != null
                                ? PromptLine(ctrl, it.Verb, it.HasVerb, it.action) : "");
                Set(action2Text, it != null
                                 ? PromptLine(Use2ControlName(), it.Verb2, it.HasVerb2, it.action2) : "");
                if (crosshair != null)
                    crosshair.color = it != null ? new Color(1f, 0.82f, 0.3f, 0.9f)
                                                 : new Color(1f, 1f, 1f, 0.4f);
            }

            // AND THE BAND, because this room now contains a bed.
            //
            // The header was place / money / date, and the slot clock moves
            // MORNING to AFTERNOON to NIGHT twice for every once it turns the
            // calendar over — so two sleeps out of three changed neither the
            // money nor the day and the screen did not repaint at all. The whole
            // answer to pressing SLEEP was a toast that expires in two and a half
            // seconds. LifeHomeScreen's own note explains why its version can get
            // away with a bare toast: "The header already carries the date and
            // the band". This one did not, and a clock-advancing verb in a scene
            // with no clock is a button that appears to do nothing.
            if (!showWallet) Set(headerText, "");
            else if (S.money != lastMoney || S.day != lastDay || S.slotIndex != lastSlot)
            {
                lastMoney = S.money;
                lastDay = S.day;
                lastSlot = S.slotIndex;
                Set(headerText, place + "   ·   " + MenuKit.Money(S.money) + "   ·   " +
                                LifeRules.DateLabel(S.day).ToUpperInvariant() + "   ·   " +
                                LifeRules.SlotNames[Mathf.Clamp(S.slotIndex, 0,
                                    LifeRules.SlotNames.Length - 1)]);
            }

            // The pad is in the gate as well as the cursor: the hint names the
            // controls the player HAS, and a pad plugged in halfway through
            // changes every one of them.
            bool captured = walker != null && walker.MouseCaptured;
            bool pad = Gamepad.current != null;
            // The invert state is IN the hint, so it has to be in the gate too:
            // a line that names the setting and then does not change when the
            // player presses the key reads as the key having done nothing.
            bool inv = LookPrefs.InvertY;
            if (captured != lastCaptured || pad != lastPad || inv != lastInvert || lastHint == null)
            {
                lastCaptured = captured;
                lastPad = pad;
                lastInvert = inv;
                lastHint = HintLines();
                Set(hintText, lastHint);
            }

            Set(toastText, Time.unscaledTime < toastUntil ? toast : "");
        }

        string HintLines()
        {
            // The look axis is the one control a player cannot work around, so
            // the key that flips it is named on every device that has a
            // keyboard — and named as LOOK Y, matching the pause-menu row, so
            // the two read as the same setting rather than two settings.
            string invert = "   ·   I INVERTS LOOK Y (" + LookPrefs.Label + ")";

            // THE button, not the USE button: it says PICK UP at the counter
            // and SLEEP at the bed now, and a hint naming a word that is not
            // on screen is a hint about a control the player cannot find.
            if (Thumbs) return "LEFT THUMB WALKS  ·  RIGHT THUMB LOOKS  ·  THE BUTTON ACTS";

            if (Gamepad.current != null)
                return "LEFT STICK MOVES   ·   RIGHT STICK LOOKS   ·   A / CROSS USES" + invert;

            string line = "WASD / ARROWS MOVE   ·   MOUSE LOOKS   ·   F OR ENTER USES" + invert;
            if (walker != null && !walker.MouseCaptured)
                line = "CLICK TO LOOK AROUND\n" + line + "   ·   ESC FREES THE MOUSE";
            return line;
        }

        /// <summary>
        /// On a touchscreen this names the BUTTON, so it says what the button
        /// says — "[USE]" only when the target has not said otherwise. On a
        /// keyboard or pad it is the in-car idiom the same scenes already show
        /// ("PRESS E — GET OUT AND WALK", "PRESS F — WHERE TO?"), so the
        /// walker's prompt and the driver's stop reading as two games.
        /// </summary>
        string UseControlName(FootTarget it)
        {
            if (Thumbs) return "[" + (it != null ? it.Verb : "USE") + "]";
            return Gamepad.current != null ? "PRESS A / CROSS —" : "PRESS F —";
        }

        /// <summary>Empty on a touchscreen: the second thumb button is
        /// labelled with the verb itself, so a bracket in front of the same
        /// words would be naming a key that device does not have.</summary>
        string Use2ControlName()
        {
            if (Thumbs) return "";
            return Gamepad.current != null ? "PRESS X / SQUARE —" : "PRESS E —";
        }

        /// <summary>
        /// One prompt line out of three words: the control, the button word,
        /// and the sentence. Empty when there is no sentence — the sentence is
        /// still the only thing that decides whether there is a control.
        ///
        /// The verb is said ONCE. On thumbs the bracket in ctrl already is the
        /// verb, so the body is the sentence alone, and a sentence that IS the
        /// verb (the bed's "SLEEP") is dropped rather than printed twice:
        /// "[SLEEP]". On a keyboard the verb leads the sentence — "PRESS F —
        /// PICK UP   ·   CLOCK ON — TAKE A RUN" — unless the sentence already
        /// starts with it ("GET IN AND DRIVE" under GET IN) or the target never
        /// named one, in which case the line is what it always was:
        /// "PRESS F — GET IN AND DRIVE". A fallback verb never leads, or every
        /// unnamed prompt on a keyboard would begin with USE.
        /// </summary>
        string PromptLine(string ctrl, string verb, bool named, string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return "";
            bool leads = sentence.StartsWith(verb, System.StringComparison.OrdinalIgnoreCase);
            string body;
            if (Thumbs)
                body = string.Equals(sentence, verb, System.StringComparison.OrdinalIgnoreCase)
                     ? "" : sentence;
            else
                body = named && !leads ? verb + "   ·   " + sentence : sentence;
            return (ctrl + "  " + body).Trim();
        }

        static void Set(Text field, string value)
        {
            if (field != null && field.text != value) field.text = value;
        }
    }
}
