using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Code-generated UGUI primitives for the LifeSim menus, following the
    /// pattern PauseMenu/TouchControls established: an overlay canvas at
    /// device resolution (ScaleWithScreenSize 1280x720) so text stays readable
    /// and targets stay finger-sized on phones, with PSX-era styling carried
    /// by palette and type rather than by the 320x240 render target.
    ///
    /// Lists paginate instead of scrolling — both simpler in code-built UGUI
    /// and what PS1-era menus actually did.
    /// </summary>
    public static class MenuKit
    {
        public static readonly Color Bg = new Color(0.05f, 0.04f, 0.09f, 1f);
        public static readonly Color PanelBg = new Color(0.11f, 0.10f, 0.17f, 0.98f);
        public static readonly Color BtnBg = new Color(0.20f, 0.19f, 0.30f, 1f);
        public static readonly Color BtnBgDisabled = new Color(0.13f, 0.12f, 0.17f, 1f);
        public static readonly Color Accent = new Color(1f, 0.80f, 0.25f);   // sunset gold
        public static readonly Color Good = new Color(0.45f, 1f, 0.55f);
        public static readonly Color Bad = new Color(1f, 0.40f, 0.36f);
        /// <summary>"Dim" is secondary, not faint. It was 55% white on a dark
        /// ground, which on a phone in daylight is simply not readable — the
        /// whole menu was reported as illegible. Secondary text earns lower
        /// CONTRAST by being cooler and slightly darker, not by fading out.</summary>
        public static readonly Color Dim = new Color(0.72f, 0.74f, 0.85f, 1f);
        public static readonly Color Line = new Color(1f, 0.80f, 0.25f, 0.55f);
        /// <summary>
        /// The panel behind the tab you are ON. A gold-TINTED dark panel, not a
        /// gold FILL, and the difference is the whole reason this constant
        /// exists — see <see cref="MarkTab"/>.
        /// </summary>
        public static readonly Color TabOnBg = new Color(0.30f, 0.25f, 0.12f, 1f);

        // ---- type scale -------------------------------------------------
        // PS1-era menus used few sizes, all generous. These are the only sizes
        // the LifeSim should use; picking arbitrary numbers per call site is how
        // a UI ends up with eleven sizes and no hierarchy.
        //
        // Raised ~20% after a second "unreadable on mobile" report. Type size is
        // only half that fix — see DesignHeight, which is what actually decides
        // how many device pixels one of these units becomes.
        public const int Title = 44;
        public const int Head = 32;
        public const int Body = 26;
        public const int Small = 22;
        public const int Tiny = 19;
        /// <summary>Nothing renders smaller than this, whatever a call site asks.</summary>
        public const int MinLabelSize = 20;

        // ---- design height ------------------------------------------------
        /// <summary>Canvas height the menus are laid out against, in UI units.
        /// The scaler matches height, so this number IS the magnification: a
        /// 891-pixel-tall phone divides by it to get the scale factor.</summary>
        public const float DesignHeightDesktop = 720f;
        /// <summary>
        /// Handhelds get a much shorter design column, which magnifies
        /// everything by the ratio.
        ///
        /// At 720 a 17-unit label came out around 21 device pixels on the
        /// reporter's phone — under 5 points physical, roughly half readable
        /// size, which is what "extremely unreadable" meant. Going to 460 plus
        /// the type bump above lands the same label near 10 points, which is
        /// ordinary phone body text.
        ///
        /// The cost is vertical room: a 460-unit column cannot show what a
        /// 720-unit one did. That is why the body scrolls now — on a phone in
        /// landscape you genuinely cannot have both a full screen of content and
        /// type you can read, so the content moves rather than the type
        /// shrinking.
        /// </summary>
        public const float DesignHeightHandheld = 560f;

        /// <summary>
        /// Which column this device gets. isMobilePlatform is the real signal
        /// (Unity sets it from the user agent on WebGL); the aspect test is a
        /// backstop for a phone that reports as desktop, since a landscape
        /// window past 1.85:1 is a handheld far more often than it is a monitor.
        /// </summary>
        public static float DesignHeight
        {
            get
            {
                // Under an override the caller is explicitly describing a device,
                // so the platform flag must not out-vote it — the editor is never
                // a mobile platform, and that is exactly the case the preview
                // tool exists to inspect.
                if (ScreenSizeOverride.y <= 0f && Application.isMobilePlatform)
                    return DesignHeightHandheld;
                return ScreenAspect >= 1.85f ? DesignHeightHandheld : DesignHeightDesktop;
            }
        }

        /// <summary>
        /// Editor-preview override: pretend the screen is this size. Zero means
        /// use the real one.
        ///
        /// Layout now depends on screen metrics, and the preview tool renders
        /// into a RenderTexture whose size Screen knows nothing about — so
        /// without this it silently validated the desktop layout at a phone's
        /// aspect and reported it as the phone. A verification instrument that
        /// can be wrong without saying so is worse than none.
        /// </summary>
        public static Vector2 ScreenSizeOverride;

        static float ScreenW => ScreenSizeOverride.x > 0f ? ScreenSizeOverride.x : Screen.width;
        static float ScreenH => ScreenSizeOverride.y > 0f ? ScreenSizeOverride.y : Screen.height;
        static float ScreenAspect => ScreenH > 0f ? ScreenW / ScreenH : 16f / 9f;

        /// <summary>
        /// Half the canvas width in UI units, for THIS screen.
        ///
        /// Changing the design height changes how many units wide the canvas is,
        /// so any layout written as absolute offsets from the centre silently
        /// walks off the edge when that height moves. Columns that ask for their
        /// budget here instead survive both the design-height switch and every
        /// aspect ratio — which is the same lesson as "anchor, never measure",
        /// applied to the horizontal axis.
        /// </summary>
        public static float HalfWidth => DesignHeight * ScreenAspect * 0.5f;

        // BUTTONS are positioned by their CENTRE. These convert an edge to that
        // centre. (Labels no longer need them — see PivotXFor below.)

        /// <summary>Button x for a box whose LEFT edge should sit at <paramref name="left"/>.</summary>
        public static float ColLeft(float left, float width) => left + width * 0.5f;
        /// <summary>Button x for a box whose RIGHT edge should sit at <paramref name="right"/>.</summary>
        public static float ColRight(float right, float width) => right - width * 0.5f;

        /// <summary>
        /// A label's rect is pivoted to match its TEXT ALIGNMENT, so the x it is
        /// given is the edge the text actually starts from: the left edge for
        /// left-aligned text, the right edge for right-aligned, the centre for
        /// centred.
        ///
        /// It used to pivot on the anchor regardless of alignment, which meant
        /// every left-aligned label was CENTRED on its x and hung half its width
        /// further left than the author wrote. With the wide labels this menu
        /// uses (500-800 units) that put whole columns off the left of the
        /// screen — the garage tab's car name and repair options among them. The
        /// call sites were all written as if x meant the left edge; now it does.
        /// </summary>
        static float PivotXFor(TextAnchor align)
        {
            switch (align)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return 0f;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return 1f;
                default:
                    return 0.5f;
            }
        }

        static Font font;
        public static Font Font =>
            font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static Canvas Canvas(Transform parent, string name, int sortOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            float h = DesignHeight;
            scaler.referenceResolution = new Vector2(h * 16f / 9f, h);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Match HEIGHT, not a 50/50 blend. This menu is laid out top-to-bottom
            // against a 720-unit column, and a blend makes that column shrink on a
            // wide screen: a 2.24:1 phone resolved to 641 logical units tall, so
            // the body panel rode up over the tab bar and covered it. Matching
            // height keeps the vertical layout exactly as designed and spends the
            // extra aspect ratio on width, where there is room for it.
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color? bg = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt;
            if (bg.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = bg.Value;
                rt = img.rectTransform;
            }
            else rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>
        /// A rect defined by its EDGES rather than by a fixed size — the only way
        /// to lay out chrome that has to survive an unknown canvas height.
        /// Offsets are in canvas units: left/right from their anchors, bottom
        /// from the bottom anchor, top from the top anchor (negative = inward).
        /// </summary>
        public static RectTransform Stretch(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            float left, float right, float bottom, float top, Color? bg = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt;
            if (bg.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = bg.Value;
                rt = img.rectTransform;
            }
            else rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, top);
            return rt;
        }

        /// <summary>
        /// Turn a rect into a vertical scroll viewport and return the CONTENT
        /// rect that children should be parented to.
        ///
        /// The header comment above used to say lists paginate rather than
        /// scroll, "what PS1-era menus actually did". That held while the design
        /// column was 720 units tall. On a handheld column of 460 it stopped
        /// holding: the blacklist board alone is ten rows, and paginating a
        /// ten-row list into three pages is worse than a flick.
        /// </summary>
        public static RectTransform ScrollBody(RectTransform viewport)
        {
            // The viewport needs a raycastable graphic or the scroll is dead.
            //
            // A ScrollRect is dragged via events the EventSystem routes from
            // whatever Graphic was under the finger. Labels here are all
            // raycastTarget = false (they must be, or they would swallow clicks
            // meant for the buttons behind them), and a bare RectTransform is
            // not hit-testable at all — so a drag on a label or on empty space
            // hit NOTHING and scrolled NOTHING. Only a drag that happened to
            // start on a button worked, which is a coin-toss the player loses:
            // the garage's repair buttons sat below the fold and could not be
            // reached, reported as "options are off screen".
            //
            // A fully transparent Image is still raycastable, and being on the
            // viewport it sits behind the content, so it only catches what the
            // content did not.
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();

            var go = new GameObject("Content");
            go.transform.SetParent(viewport, false);
            var content = go.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            // Height is meaningless until FitScrollContent runs; deliberately
            // NOT read off viewport.rect, which has not resolved this frame.
            content.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            // Clamped, not elastic: a menu that bounces reads as a web page.
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.decelerationRate = 0.12f;
            return content;
        }

        /// <summary>
        /// Size a scroll content rect to whatever was just built into it.
        ///
        /// Measures from the children's ASSIGNED anchoredPosition and sizeDelta,
        /// never from rect.height — those are values we set ourselves this frame,
        /// while rect is whatever the layout system last resolved, which for a
        /// rect created this frame is nothing. Reading rect here would be the
        /// same stale-rect trap that produced the bunched tab bar and the
        /// overlapping wizard rows.
        /// </summary>
        /// <summary>
        /// Slack left under the last row of a scroll page.
        ///
        /// Named because a page that has to KNOW whether it fits must subtract
        /// it: content is sized to (lowest row + this), so a column filled
        /// exactly to the bottom edge of its viewport still comes out one
        /// padding taller than the viewport and scrolls. The usable budget is
        /// viewport MINUS this, not viewport.
        /// </summary>
        public const float ScrollPad = 28f;

        public static void FitScrollContent(RectTransform content, float viewportHeight,
                                            float padding = ScrollPad)
        {
            float lowest = 0f;
            for (int i = 0; i < content.childCount; i++)
            {
                if (!(content.GetChild(i) is RectTransform rt)) continue;
                // Only top-anchored children describe a top-down flow; anything
                // else is anchored to an edge and does not extend the column.
                if (rt.anchorMin.y < 0.999f || rt.anchorMax.y < 0.999f) continue;
                float bottom = rt.anchoredPosition.y - rt.sizeDelta.y * rt.pivot.y;
                if (bottom < lowest) lowest = bottom;
            }
            // Never shorter than the viewport: a content rect smaller than what
            // shows it lets ScrollRect drift the whole page around.
            content.sizeDelta = new Vector2(0f, Mathf.Max(-lowest + padding, viewportHeight));
        }

        public static Text Label(Transform parent, string text, int size,
            Vector2 anchor, Vector2 pos, TextAnchor align = TextAnchor.MiddleLeft,
            Color? color = null, float width = 560f, float height = 40f, bool bold = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            // Hard floor on type size. This menu is played on a phone held at
            // arm's length, and a number of call sites were asking for 13-15,
            // which is fine on a monitor and unreadable on a handset. Enforcing
            // it here fixes every screen at once rather than trusting each call.
            t.fontSize = Mathf.Max(size, MinLabelSize);
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            // A hard outline on all four sides rather than a drop shadow: PS1
            // menus outlined their type because it holds up against any
            // background, and it reads as crisper than a soft offset shadow at
            // the small sizes a phone actually renders.
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.95f);
            ol.effectDistance = new Vector2(1.6f, -1.6f);
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            // Pivot X follows the ALIGNMENT so x means the text's own edge;
            // pivot Y still follows the anchor, because the tabs stack downward
            // from the top and rely on that.
            rt.pivot = new Vector2(PivotXFor(align), anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, height);
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 anchor,
            Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick,
            int fontSize = Body, Color? bg = null)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg ?? BtnBg;
            var rt = img.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // A hairline top edge. PS1 interfaces leaned on hard 1px rules to
            // separate blocks — no gradients, no soft shadows, no rounded
            // corners — and it is what makes a flat colour field read as a
            // deliberate panel rather than as an untextured rectangle.
            bool enabled = onClick != null;
            var edge = new GameObject("Edge");
            edge.transform.SetParent(go.transform, false);
            var ei = edge.AddComponent<Image>();
            ei.color = enabled ? Line : new Color(1f, 1f, 1f, 0.10f);
            ei.raycastTarget = false;
            var ert = ei.rectTransform;
            ert.anchorMin = new Vector2(0f, 1f); ert.anchorMax = new Vector2(1f, 1f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.offsetMin = new Vector2(0f, -2f); ert.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.45f);
            colors.pressedColor = new Color(1f, 0.80f, 0.30f);
            // The pad cursor. UGUI's default selectedColor is 0.96 grey, which
            // against a white normalColor is invisible — so once these menus
            // grew keyboard/pad navigation there was a cursor moving around
            // that nobody could see. Gold, and brighter than the mouse hover,
            // because selection is where the pad is and hover is only where the
            // mouse happens to rest.
            colors.selectedColor = new Color(1.60f, 1.30f, 0.60f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = colors;
            if (enabled) btn.onClick.AddListener(onClick);
            btn.interactable = enabled;

            var t = Label(go.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter,
                enabled ? Color.white : new Color(0.60f, 0.60f, 0.68f),
                size.x, size.y, bold: true);
            // Stretch the caption to the button rather than freezing it at the
            // size passed in. Callers legitimately re-anchor a button after
            // creation (the tab bar divides itself by fraction), and a label
            // pinned to the original size silently collapsed to nothing.
            var lrt = t.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(6f, 0f);
            lrt.offsetMax = new Vector2(-6f, 0f);
            return btn;
        }

        /// <summary>
        /// Mark a tab button as the one the player is looking at.
        ///
        /// The first version of this was `bg = Accent` plus `text = black`, and
        /// it was reported as unreadable. Three separate things were fighting:
        ///
        ///   * Every <see cref="Label"/> carries a hard black Outline on all
        ///     four sides. Black type wearing a black outline is drawn four
        ///     times around glyphs the same colour as the outline, so the
        ///     strokes thicken and the counters fill in — the "smudged" look.
        ///   * A Button MULTIPLIES its image by the ColorBlock, and the pad
        ///     cursor's tint is (1.60, 1.30, 0.60). Against a gold fill that
        ///     clips to pure saturated yellow, so the selected tab lost its
        ///     shading the moment the cursor touched it.
        ///   * A solid gold block next to seven dark ones is the loudest thing
        ///     on a screen whose accent colour also means "this is the value
        ///     that changed".
        ///
        /// So the selection is carried by a DARK gold-tinted panel, gold bold
        /// type, and a hard rule along the bottom edge. The rule is the part
        /// that cannot be washed out by any colour multiplier, which is what
        /// makes it the signal rather than the decoration.
        /// </summary>
        public static void MarkTab(Button b, bool on)
        {
            if (b == null) return;

            if (b.targetGraphic is Image img) img.color = on ? TabOnBg : BtnBg;

            var c = b.colors;
            // Gentler tints on the selected tab: it starts brighter, so the
            // same multipliers that read as "the cursor is here" on a dark cell
            // read as "this cell is on fire" on this one.
            c.highlightedColor = on ? new Color(1.20f, 1.18f, 1.12f)
                                    : new Color(1.35f, 1.35f, 1.45f);
            c.selectedColor = on ? new Color(1.34f, 1.24f, 1.00f)
                                 : new Color(1.60f, 1.30f, 0.60f);
            b.colors = c;

            var t = b.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = on ? Accent : Color.white;
                var ol = t.GetComponent<Outline>();
                if (ol != null)
                    ol.effectColor = new Color(0f, 0f, 0f, on ? 0.75f : 0.95f);
            }

            // Found by name rather than kept in a field: these buttons are
            // rebuilt by the page that owns them and a cached reference would
            // outlive the object it points at.
            var mark = b.transform.Find("TabMark");
            if (!on)
            {
                if (mark != null) mark.gameObject.SetActive(false);
                return;
            }
            if (mark == null)
            {
                var go = new GameObject("TabMark");
                go.transform.SetParent(b.transform, false);
                var mi = go.AddComponent<Image>();
                mi.color = Accent;
                mi.raycastTarget = false;
                var mrt = mi.rectTransform;
                mrt.anchorMin = new Vector2(0f, 0f);
                mrt.anchorMax = new Vector2(1f, 0f);
                mrt.pivot = new Vector2(0.5f, 0f);
                mrt.offsetMin = Vector2.zero;
                mrt.offsetMax = new Vector2(0f, 4f);
                mark = go.transform;
            }
            mark.gameObject.SetActive(true);
            // Under the caption, which is stretched over the whole button: a
            // 4-unit rule drawn first would be hidden by the label's own
            // (transparent) rect only if the label came later, so pin the order.
            mark.SetAsLastSibling();
        }

        /// <summary>
        /// Scanline overlay. Cheap, non-interactive, and the single strongest
        /// signal that a crisp modern UI belongs to a PS1-era game — the menus
        /// otherwise read as a different product from the 320x240 race view.
        /// </summary>
        public static void Scanlines(Transform parent, float alpha = 0.16f)
        {
            var go = new GameObject("Scanlines");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = ScanTex();
            img.color = new Color(1f, 1f, 1f, alpha);
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            // Tile against the CANVAS height, not Screen.height: the two differ
            // whenever the scaler is active, and using the screen made the line
            // pitch drift with resolution until it washed out to flat grey.
            go.AddComponent<ScanlineTiler>();
        }

        /// <summary>Keeps the scanline pitch at one line per 3 canvas units as
        /// the rect resolves and on any later resize.</summary>
        class ScanlineTiler : MonoBehaviour
        {
            RawImage img;
            RectTransform rt;
            float lastHeight = -1f;

            void Awake() { img = GetComponent<RawImage>(); rt = GetComponent<RectTransform>(); }

            void LateUpdate()
            {
                float h = rt.rect.height;
                if (h <= 0f || Mathf.Approximately(h, lastHeight)) return;
                lastHeight = h;
                img.uvRect = new Rect(0f, 0f, 1f, h / 3f);
            }
        }

        static Texture2D scanTex;
        static Texture2D ScanTex()
        {
            if (scanTex != null) return scanTex;
            scanTex = new Texture2D(1, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            scanTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
            scanTex.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            scanTex.Apply();
            return scanTex;
        }

        /// <summary>Money with RG2's sign convention: green when the delta is
        /// income, red when it is a cost.</summary>
        public static string Money(int amount) => "$" + amount.ToString("N0");
    }
}
