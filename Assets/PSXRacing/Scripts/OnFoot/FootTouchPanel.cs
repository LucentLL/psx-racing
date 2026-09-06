using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// Thumbs. A stick on the left, look-drag on the right, and one USE button
    /// where the right thumb already is.
    ///
    /// Touches are read straight off <see cref="Touchscreen"/> and hit-tested
    /// by hand rather than routed through UGUI events. The reason is the same
    /// one the driving controls found: a look-drag has to work anywhere on its
    /// half of the screen, including on top of whatever else is drawn there,
    /// and an EventSystem that consumes the press for the topmost graphic makes
    /// that impossible without covering the screen in invisible raycast
    /// targets. Each touch is claimed ONCE, by where it STARTED, and keeps its
    /// job until it lifts — which is what makes walking and looking at the same
    /// time work.
    /// </summary>
    public class FootTouchPanel : MonoBehaviour
    {
        public FirstPersonWalk walker;
        public FootInteractor interactor;

        /// <summary>Force the panel on for testing on a desktop.</summary>
        public bool forceShow;

        /// <summary>True while a pointer is on the USE button, so the walker
        /// does not treat that click as "grab the mouse".</summary>
        public static bool PointerOverUI { get; private set; }

        public bool Visible { get; private set; }

        /// <summary>Screen fraction the walk stick owns. The rest is look.
        /// </summary>
        const float StickZoneX = 0.45f;
        /// <summary>Degrees of turn per screen-height of drag. Tuned against
        /// the driving game's wheel: about a third of a turn for a full swipe.
        /// </summary>
        const float LookDegPerScreen = 260f;
        /// <summary>Stick throw, in screen-height fractions. A thumb does not
        /// travel far.</summary>
        const float StickRadiusFrac = 0.13f;

        RectTransform stickBase, stickKnob, useRect, use2Rect;
        Text useLabel, use2Label;
        Image useImage, use2Image;
        Vector2 stickHome;

        int stickTouch = -1, lookTouch = -1;
        Vector2 stickOrigin;

        Canvas canvas;

        /// <summary>
        /// Set by <see cref="ForecourtMode"/> on the panel it builds itself, and
        /// by nobody else: this one belongs to a player who can be sitting in a
        /// car, so it has to stand down while they are.
        ///
        /// DECLARED RATHER THAN GUESSED. The panel could look for a
        /// ForecourtMode in the scene and infer the same answer, and it would
        /// be right in all four scene families today — but the failure mode of
        /// a wrong guess is a soft lock. The walk-in scenes (the garage, the
        /// pizzeria, the seller's driveway) carry a panel and no ForecourtMode,
        /// and there this IS the player's only set of controls: losing it is
        /// "the difference between walking out of the room and reloading the
        /// page", in SetVisible's own words below. Whoever owns a car says so;
        /// everybody else gets the panel they have always had.
        ///
        /// NonSerialized, so the FootTouchPanel already baked into Garage.unity
        /// is byte-for-byte what it was and no scene needs rebuilding for this.
        /// </summary>
        [System.NonSerialized] public bool ownedByCar;

        /// <summary>Is the player on their feet? The flag every other overlay
        /// in this game already reads — RaceHUD, GaugeCluster, CockpitView,
        /// StuckRecovery and TouchControls all gate on it. This panel was the
        /// one that did not.</summary>
        bool Afoot => !ownedByCar || ForecourtMode.OnFoot;

        void Awake()
        {
            Build();
            SetVisible(forceShow || Application.isMobilePlatform ||
                       SystemInfo.deviceType == DeviceType.Handheld);
        }

        /// <summary>
        /// Show or hide the whole panel. Reversible, and it has to be: the
        /// device test at Awake is the same one the driving controls use and it
        /// is WRONG on a tablet browser asked for the desktop site, where
        /// isMobilePlatform is false and deviceType is Desktop. Out on the
        /// circuit that costs the player a steering wheel they can live
        /// without — a keyboard is one tap away on those devices. In the
        /// garage it is the difference between walking out of the room and
        /// reloading the page, because the door is something you WALK to.
        /// </summary>
        void SetVisible(bool v)
        {
            Visible = v;
            Apply();
        }

        /// <summary>Two independent questions, one canvas: does this device
        /// have thumbs (<see cref="Visible"/>, which FootScreen also reads to
        /// choose its wording, so it must keep meaning only that), and is the
        /// player on their feet.</summary>
        void Apply()
        {
            bool want = Visible && Afoot;
            if (canvas != null && canvas.enabled != want) canvas.enabled = want;
        }

        void Build()
        {
            var canvasGO = new GameObject("GarageTouchCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            stickBase = Disc(canvasGO.transform, "StickBase", 190f,
                             new Color(1f, 1f, 1f, 0.10f));
            stickBase.anchorMin = stickBase.anchorMax = new Vector2(0f, 0f);
            stickBase.pivot = new Vector2(0.5f, 0.5f);
            stickBase.anchoredPosition = stickHome = new Vector2(150f, 150f);

            stickKnob = Disc(stickBase, "StickKnob", 84f, new Color(1f, 0.85f, 0.35f, 0.55f));
            stickKnob.anchorMin = stickKnob.anchorMax = new Vector2(0.5f, 0.5f);
            stickKnob.pivot = new Vector2(0.5f, 0.5f);
            stickKnob.anchoredPosition = Vector2.zero;

            var useGO = new GameObject("Use");
            useGO.transform.SetParent(canvasGO.transform, false);
            useImage = useGO.AddComponent<Image>();
            useImage.color = new Color(0.55f, 0.36f, 0.02f, 0.82f);
            useRect = useImage.rectTransform;
            useRect.anchorMin = useRect.anchorMax = new Vector2(1f, 0f);
            useRect.pivot = new Vector2(1f, 0f);
            useRect.anchoredPosition = new Vector2(-38f, 62f);
            useRect.sizeDelta = new Vector2(240f, 96f);

            useLabel = ButtonLabel(useGO.transform, font, 26, "USE");

            // The second verb, above USE rather than beside it: the right edge
            // of a phone screen is where the thumb already is, and two buttons
            // side by side there would put one of them under the base of the
            // thumb where it cannot be pressed without lifting the hand.
            var use2GO = new GameObject("Use2");
            use2GO.transform.SetParent(canvasGO.transform, false);
            use2Image = use2GO.AddComponent<Image>();
            use2Image.color = new Color(0.16f, 0.30f, 0.34f, 0.85f);
            use2Rect = use2Image.rectTransform;
            use2Rect.anchorMin = use2Rect.anchorMax = new Vector2(1f, 0f);
            use2Rect.pivot = new Vector2(1f, 0f);
            use2Rect.anchoredPosition = new Vector2(-38f, 170f);
            use2Rect.sizeDelta = new Vector2(240f, 74f);
            use2Label = ButtonLabel(use2GO.transform, font, 22, "INSPECT");
        }

        static Text ButtonLabel(Transform parent, Font font, int size, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.text = text;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return t;
        }

        void Update()
        {
            PointerOverUI = false;
            // Every frame, because OnFoot flips inside ForecourtMode's
            // coroutines and nobody calls in here to say so.
            Apply();

            // BACK IN THE CAR, AND EVERYTHING BELOW IS FOR SOMEBODY STANDING UP.
            //
            // The forecourt does not destroy its rig when the player gets in —
            // it deactivates the WALKER (ForecourtMode.GetIn), and this panel
            // hangs off the scene's systems object rather than off the walker,
            // so it never receives the OnDisable that FirstPersonWalk stands
            // itself down in. Left running it draws a USE button over the
            // throttle pedal and a walk stick over the steering wheel —
            // reported in those words after a pizza pickup — and it hit-tests
            // every pedal press into interactor.touchUse, which then fires the
            // moment the player next steps out.
            //
            // ABOVE the reveal-on-touch below, and not merged into it: a
            // throttle tap must not be able to flip Visible true and light the
            // canvas back up. That is the exact trap TouchControls.Update
            // documents in its own mirror-image guard.
            if (!Afoot)
            {
                stickTouch = lookTouch = -1;
                // BOTH fields, and they are not symmetric: FirstPersonWalk
                // OVERWRITES from externalMove every frame but DRAINS
                // externalLook, which this panel adds to. A sum left standing
                // is a sum that lands in one frame the next time somebody
                // steps out of the car.
                if (walker != null)
                {
                    walker.externalMove = Vector2.zero;
                    walker.externalLook = Vector2.zero;
                }
                if (interactor != null) { interactor.touchUse = false; interactor.touchUse2 = false; }
                return;
            }

            // A finger on the glass is proof, and it outranks whatever the
            // platform claimed at boot. Revealed on the first touch rather than
            // never, so a device the device test got wrong is not a room the
            // player cannot leave.
            if (!Visible && Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.isPressed)
                SetVisible(true);

            if (!Visible) return;

            var target = interactor != null ? interactor.Current : null;
            bool offer = target != null && !string.IsNullOrEmpty(target.action);
            if (useImage.gameObject.activeSelf != offer) useImage.gameObject.SetActive(offer);

            bool offer2 = target != null && !string.IsNullOrEmpty(target.action2);
            if (use2Image.gameObject.activeSelf != offer2) use2Image.gameObject.SetActive(offer2);
            // The button says the verb, so a car that offers INSPECT and a
            // fixture that offers something else are not the same button with
            // different consequences. First word only — a thumb button is not
            // a place for a sentence.
            if (offer2)
            {
                string want = FirstWords(target.action2);
                if (use2Label.text != want) use2Label.text = want;
            }

            Vector2 move = Vector2.zero;
            Vector2 look = Vector2.zero;
            bool stillHaveStick = false, stillHaveLook = false;

            var screen = Touchscreen.current;
            if (screen != null)
            {
                var touches = screen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    var phase = t.phase.ReadValue();
                    bool live = phase == UnityEngine.InputSystem.TouchPhase.Began ||
                                phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                                phase == UnityEngine.InputSystem.TouchPhase.Stationary;
                    if (!live) continue;

                    int id = t.touchId.ReadValue();
                    Vector2 pos = t.position.ReadValue();

                    if (phase == UnityEngine.InputSystem.TouchPhase.Began)
                    {
                        if (offer && Contains(useRect, pos))
                        {
                            if (interactor != null) interactor.touchUse = true;
                            continue;
                        }
                        if (offer2 && Contains(use2Rect, pos))
                        {
                            if (interactor != null) interactor.touchUse2 = true;
                            continue;
                        }
                        if (pos.x < Screen.width * StickZoneX && stickTouch < 0)
                        {
                            stickTouch = id;
                            stickOrigin = pos;
                        }
                        else if (lookTouch < 0) lookTouch = id;
                        // NO `continue` HERE. Falling through is what registers
                        // the claim on the frame it is made: the two "do I still
                        // have it" flags below are recomputed from scratch every
                        // frame and cleared at the bottom if nothing set them,
                        // so a touch that claimed a slot and then skipped the
                        // rest of the loop had its slot released the same frame
                        // it took it. The next frame the touch is Moved, not
                        // Began, so it never claimed again — which left a phone
                        // player in the garage unable to walk, turn, or reach
                        // the door. Everything below is a no-op on this frame
                        // anyway: the drag is zero and the delta is zero.
                    }

                    if (id == stickTouch)
                    {
                        stillHaveStick = true;
                        float radius = Mathf.Max(1f, Screen.height * StickRadiusFrac);
                        Vector2 d = (pos - stickOrigin) / radius;
                        if (d.sqrMagnitude > 1f) d.Normalize();
                        move = d;
                    }
                    else if (id == lookTouch)
                    {
                        stillHaveLook = true;
                        Vector2 d = t.delta.ReadValue();
                        float deg = LookDegPerScreen / Mathf.Max(1f, Screen.height);
                        look += new Vector2(d.x * deg, d.y * deg);
                    }
                }
            }

            if (!stillHaveStick) stickTouch = -1;
            if (!stillHaveLook) lookTouch = -1;

            // Mouse, for testing the panel on a desktop with forceShow. The
            // USE button is the only part worth driving this way — a mouse
            // already has a better stick and a better look control.
            var mouse = Mouse.current;
            if (mouse != null && offer && Contains(useRect, mouse.position.ReadValue()))
            {
                PointerOverUI = true;
                if (mouse.leftButton.wasPressedThisFrame && interactor != null)
                    interactor.touchUse = true;
            }
            if (mouse != null && offer2 && Contains(use2Rect, mouse.position.ReadValue()))
            {
                PointerOverUI = true;
                if (mouse.leftButton.wasPressedThisFrame && interactor != null)
                    interactor.touchUse2 = true;
            }

            if (walker != null)
            {
                // A PAGE ON SCREEN IS NOT A ROOM YOU ARE LOOKING ROUND. The
                // store counter and the wreck screen switch the WALKER off and
                // leave it active, so this panel keeps running over the top of
                // an open menu — and because externalLook is accumulated here
                // and drained by FirstPersonWalk.Look, every drag on that page
                // piled up and swung the view the moment it closed.
                bool live = walker.enabled;
                walker.externalMove = live ? move : Vector2.zero;
                if (live) walker.externalLook += look;
                else walker.externalLook = Vector2.zero;
            }

            // The stick base follows the thumb once it has one, so a thumb that
            // landed off-centre is not fighting a control drawn somewhere else.
            if (stickTouch >= 0)
            {
                stickBase.anchoredPosition = ScreenToCanvas(stickOrigin, stickBase);
                stickKnob.anchoredPosition = move * (stickBase.sizeDelta.x * 0.5f);
            }
            else
            {
                stickBase.anchoredPosition = stickHome;
                stickKnob.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>Screen pixels to the anchored position of a rect anchored
        /// at the bottom-left corner. The canvas scales, so a pixel is not a
        /// canvas unit and the ratio is whatever the scaler settled on this
        /// frame.</summary>
        static Vector2 ScreenToCanvas(Vector2 screen, RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            float s = canvas != null ? canvas.scaleFactor : 1f;
            if (s <= 0f) s = 1f;
            return screen / s;
        }

        /// <summary>The first two words of a prompt, upper-cased — "INSPECT
        /// THIS CAR" becomes "INSPECT THIS". Long enough to be a verb with an
        /// object, short enough to fit a thumb button at 22pt.</summary>
        static string FirstWords(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            var parts = action.Split(' ');
            string s = parts.Length > 1 ? parts[0] + " " + parts[1] : parts[0];
            return s.ToUpperInvariant();
        }

        static bool Contains(RectTransform rect, Vector2 screenPoint) =>
            rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

        /// <summary>A soft filled circle, generated rather than imported: one
        /// 64x64 disc is cheaper to make than to route through the asset
        /// pipeline, and it is the same trick the car viewer uses for its blob
        /// shadow.</summary>
        static Sprite discSprite;
        static Sprite Disc()
        {
            if (discSprite != null) return discSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                    float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - r) * 6f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return discSprite;
        }

        static RectTransform Disc(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = Disc();
            img.color = color;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img.rectTransform;
        }
    }
}
