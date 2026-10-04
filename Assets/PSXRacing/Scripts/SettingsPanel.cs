using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using MenuKit = PSXRacing.LifeSim.MenuKit;

namespace PSXRacing
{
    /// <summary>
    /// THE SETTINGS MENU (owner, 2026-10-04: "The game needs a proper menu with
    /// tabs for Gameplay, Visuals, Audio, Settings"). One page with four tabs
    /// over <see cref="SettingsCatalog"/>, opened from the pause menu and from
    /// both front ends' OPTIONS pages, so every setting lives in one place.
    ///
    /// A page of its own over its host, like the debug bench and CREDITS: its
    /// own MenuKit canvas (matched to HEIGHT, the GT2 charcoal and blueprint
    /// grid), and while it is up it owns the keys - its host checks
    /// <see cref="IsOpen"/> and <see cref="ClosedFrame"/> and stands back.
    ///
    ///   tabs     : tap or click one; LB / RB, Q / E or Page Up / Down step
    ///              through them (Q / E are the gear keys, but the car takes no
    ///              input while the pause menu is up).
    ///   rows     : a STEP row is a button whose press moves the setting on and
    ///              rewrites its value in place (no rebuild, so the cursor stays
    ///              where it is); a SLIDER is a UGUI Slider - dragged with a
    ///              finger or the mouse, walked a notch at a time with left /
    ///              right on a pad, the d-pad or the arrow keys.
    ///   help     : the line under the rows says what the row under the cursor
    ///              (or the mouse) does.
    ///   back     : B / Circle, Escape and START close it, as they close every
    ///              page over the pause menu; BACK does it for a finger.
    ///
    /// Laid out by anchors and fixed steps from the top - nothing measures a
    /// rect - and sized for the 560-unit column a phone in landscape gets: the
    /// longest tab is six rows.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        /// <summary>The drive's extras (camera, debug), or null in the front end.</summary>
        public SettingsHost host;
        public System.Action onClosed;
        /// <summary>True while something the host opened over this page (the
        /// debug bench) owns the keys.</summary>
        public System.Func<bool> blocked;

        public bool IsOpen { get; private set; }
        /// <summary>The frame this page closed on: B / Escape on that frame was
        /// the press that closed it, and must not also back the host out.</summary>
        public int ClosedFrame { get; private set; } = -1;
        public SettingsTab Tab => tab;

        /// <summary>Over the pause menu's 200 and every front-end canvas,
        /// under the bench and CREDITS at 210, which open on top of it.</summary>
        public const int SortOrder = 205;

        // Layout, in MenuKit units from the top of a 560-unit column (a phone
        // in landscape); a taller column centres the block (see Off).
        const float TitleY = -12f, TabY = -62f, TabH = 44f, TabGap = 8f;
        const float RowTop = -120f, RowH = 40f, RowStep = 48f;
        const float HelpY = -412f, HelpH = 72f;
        const float FooterY = -504f, FooterH = 42f, BackW = 200f, PadX = 18f;
        const float BlockH = 560f;

        Canvas canvas;
        RectTransform body;
        Text help;
        Button back;
        MenuNavWatch watch;
        CreditsPanel credits;
        SettingsTab tab;
        float liveTimer;
        internal GameObject lastSel;
        readonly List<Button> tabs = new List<Button>();
        readonly List<Selectable> rows = new List<Selectable>();
        readonly List<KeyValuePair<SettingItem, Text>> live = new List<KeyValuePair<SettingItem, Text>>();
        readonly List<MenuNavWatch> stoodDown = new List<MenuNavWatch>();

        static float Width => Mathf.Min(760f, MenuKit.HalfWidth * 2f - 80f);
        static float Off => Mathf.Max(0f, (MenuKit.DesignHeight - BlockH) * 0.5f);

        public void Open(SettingsTab at)
        {
            if (IsOpen) { Show(at, false); return; }
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MenuKit.EnsureEventSystem();
            // The host's nav watch would pull the cursor back onto its own
            // rows; it stands down while this page is up (as for CREDITS).
            stoodDown.Clear();
            foreach (var w in FindObjectsByType<MenuNavWatch>(FindObjectsInactive.Exclude))
                if (w != null && w.enabled) { w.enabled = false; stoodDown.Add(w); }

            canvas = MenuKit.Canvas(transform, "SettingsCanvas", SortOrder);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.08f, 0.08f, 0.08f, 0.94f));
            MenuKit.GridBackdrop(canvas.transform);
            BuildChrome();
            Show(at, false);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            if (credits != null && credits.IsOpen) credits.Close();
            foreach (var w in stoodDown) if (w != null) w.enabled = true;
            stoodDown.Clear();
            if (canvas != null) Kill(canvas.gameObject);
            canvas = null; body = null; help = null; back = null; watch = null; lastSel = null;
            tabs.Clear(); rows.Clear(); live.Clear();
            onClosed?.Invoke();
        }

        void OnDisable() { if (IsOpen) Close(); }

        /// <summary>Put the cursor back where it was - after a page this one
        /// opened (the bench, CREDITS) closes.</summary>
        public void Refocus()
        {
            if (!IsOpen) return;
            if (lastSel != null && lastSel.activeInHierarchy) MenuNav.Select(lastSel.GetComponent<Selectable>());
            else MenuNav.Select(rows.Count > 0 ? rows[0] : back);
        }

        void OpenCredits()
        {
            if (!IsOpen) return;
            if (credits == null)
            {
                credits = gameObject.AddComponent<CreditsPanel>();
                credits.onClosed = () => { if (this != null) Refocus(); };
            }
            credits.Open();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (credits != null && (credits.IsOpen || credits.ClosedFrame == Time.frameCount)) return;
            if (blocked != null && blocked()) return;

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            int step = 0;
            if (pad != null)
            {
                if (pad.rightShoulder.wasPressedThisFrame) step = 1;
                else if (pad.leftShoulder.wasPressedThisFrame) step = -1;
            }
            if (step == 0 && kb != null)
            {
                if (kb.eKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame) step = 1;
                else if (kb.qKey.wasPressedThisFrame || kb.pageUpKey.wasPressedThisFrame) step = -1;
            }
            if (step != 0)
            {
                Show((SettingsTab)(((int)tab + step + TabCount) % TabCount), false);
                return;
            }
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
                (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            {
                Close();
                return;
            }

            // Values the world can change under the page: the browser takes
            // fullscreen away on its own, and the camera keys still work on a
            // keyboard. Four times a second, on unscaled time (the pause menu
            // runs at timeScale 0).
            liveTimer += Time.unscaledDeltaTime;
            if (liveTimer > 0.25f)
            {
                liveTimer = 0f;
                foreach (var l in live)
                {
                    if (l.Value == null || l.Key.value == null) continue;
                    string s = l.Key.value();
                    if (l.Value.text != s) l.Value.text = s;
                }
            }
        }

        static int TabCount => SettingsCatalog.TabNames.Length;

        // ---- layout -----------------------------------------------------------

        void BuildChrome()
        {
            var top = new Vector2(0.5f, 1f);
            float w = Width, off = Off;
            var title = MenuKit.Label(canvas.transform, "OPTIONS", 34, top, new Vector2(0f, TitleY - off),
                                      TextAnchor.MiddleCenter, MenuKit.Accent, 600f, 44f, bold: true);
            title.name = "Title";

            var bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(canvas.transform, false);
            body = (RectTransform)bodyGO.transform;
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = Vector2.zero; body.offsetMax = Vector2.zero;

            // The strip: four equal cells across the column, by arithmetic on
            // the column width rather than by measuring anything.
            tabs.Clear();
            float cell = (w - (TabCount - 1) * TabGap) / TabCount;
            for (int i = 0; i < TabCount; i++)
            {
                int idx = i;
                float x = -w * 0.5f + cell * 0.5f + i * (cell + TabGap);
                var tb = MenuKit.Button(canvas.transform, SettingsCatalog.TabNames[i], top,
                                        new Vector2(x, TabY - off), new Vector2(cell, TabH),
                                        () => Show((SettingsTab)idx, true), MenuKit.Small);
                tb.name = "Tab_" + SettingsCatalog.TabNames[i];
                Tag(tb.gameObject, SettingsCatalog.TabBlurbs[i]);
                tabs.Add(tb);
            }

            // The help line: what the row under the cursor does. Three lines
            // of room at the type floor; the longest sentence takes two on the
            // narrowest column (a 4:3 tablet's).
            help = MenuKit.Label(canvas.transform, "", MenuKit.MinLabelSize, top,
                                 new Vector2(-w * 0.5f, HelpY - off), TextAnchor.UpperLeft,
                                 MenuKit.Dim, w, HelpH);
            help.horizontalOverflow = HorizontalWrapMode.Wrap;
            help.name = "Help";

            back = MenuKit.Button(canvas.transform, "BACK", top,
                                  new Vector2(-w * 0.5f + BackW * 0.5f, FooterY - off),
                                  new Vector2(BackW, FooterH), Close, MenuKit.Small);
            back.name = "Btn_BACK";
            Tag(back.gameObject, "Back to where you came from. B, Circle or Escape does the same.");

            var hint = MenuKit.Label(canvas.transform, "Q / E  ·  LB / RB  PAGES     B / ESC  BACK",
                                     MenuKit.MinLabelSize, top, new Vector2(w * 0.5f, FooterY - off),
                                     TextAnchor.MiddleRight, MenuKit.Dim, w - BackW - 30f, FooterH);
            hint.name = "Hint";
            MenuKit.FitOneLine(hint, w - BackW - 30f);
        }

        /// <summary>Turn to a tab: mark it, rebuild the rows, rewire the pad.
        /// <paramref name="cursorOnTab"/> keeps the cursor on the strip (the
        /// tab was pressed); otherwise it lands on the first row (the page was
        /// opened at this tab, or stepped to with a shoulder button).</summary>
        public void Show(SettingsTab t, bool cursorOnTab)
        {
            tab = t;
            if (!IsOpen || body == null) return;
            for (int i = 0; i < tabs.Count; i++) MenuKit.MarkTab(tabs[i], i == (int)t);

            for (int i = body.childCount - 1; i >= 0; i--) Kill(body.GetChild(i).gameObject);
            rows.Clear();
            live.Clear();

            float y = RowTop - Off;
            foreach (var it in SettingsCatalog.Items(host, t, OpenCredits))
            {
                rows.Add(it.IsSlider ? SliderRow(it, y) : StepRow(it, y));
                y -= RowStep;
            }

            // Creation order IS the layout here - one column under one strip -
            // so the chain needs no geometric pass: down off a tab to the first
            // row, up off the first row to the tab you are on, BACK at the foot.
            // Left / right on a row are left empty on purpose: that is what
            // lets a selected Slider take them as "less" and "more".
            var col = new List<Selectable>(rows) { back };
            MenuNav.Row(new List<Selectable>(tabs));
            MenuNav.Column(col);
            MenuNav.Join(new List<Selectable>(tabs), col, tabs[(int)t]);
            Selectable first = rows.Count > 0 ? rows[0] : (Selectable)back;
            if (watch == null) watch = MenuNav.Watch(canvas.gameObject, first);
            else watch.fallback = first;

            Selectable sel = cursorOnTab ? tabs[(int)t] : first;
            lastSel = sel.gameObject;
            MenuNav.Select(sel);
            var tag = sel.GetComponent<SettingsHelpTag>();
            ShowHelp(tag != null ? tag.text : "");
        }

        internal void ShowHelp(string s)
        {
            if (help != null && help.text != s) help.text = s ?? "";
        }

        void Tag(GameObject go, string text)
        {
            var t = go.AddComponent<SettingsHelpTag>();
            t.panel = this;
            t.text = text;
        }

        Selectable StepRow(SettingItem it, float y)
        {
            float w = Width;
            Text val = null;
            var item = it;
            var b = MenuKit.Button(body, it.name, new Vector2(0.5f, 1f), new Vector2(0f, y),
                                   new Vector2(w, RowH), () => Press(item, val), MenuKit.Small);
            b.name = "Btn_" + it.name;
            // The name on the left half, the value on the right half in amber:
            // the two halves never share a pixel, whatever either says.
            var cap = b.GetComponentInChildren<Text>();
            cap.alignment = TextAnchor.MiddleLeft;
            cap.rectTransform.offsetMin = new Vector2(PadX, 0f);
            cap.rectTransform.offsetMax = new Vector2(-w * 0.5f, 0f);
            val = MenuKit.Label(b.transform, it.value != null ? it.value() : "", MenuKit.Small,
                                new Vector2(1f, 0.5f), new Vector2(-PadX, 0f), TextAnchor.MiddleRight,
                                MenuKit.Accent, w * 0.5f - PadX * 2f, RowH, bold: true);
            val.name = "Value";
            live.Add(new KeyValuePair<SettingItem, Text>(it, val));
            Tag(b.gameObject, it.blurb);
            return b;
        }

        void Press(SettingItem it, Text val)
        {
            it.apply?.Invoke();
            PlayerPrefs.Save();
            if (IsOpen && val != null && it.value != null) val.text = it.value();
        }

        /// <summary>
        /// A slider row: name on the left, the track in the middle, the value
        /// on the right. The Slider sits on the whole row so the pad cursor
        /// tints the whole row gold like a button's, but only the TRACK takes
        /// a pointer - a tap on the name must not throw the value to one end.
        /// </summary>
        Selectable SliderRow(SettingItem it, float y)
        {
            float w = Width;
            const float NameW = 210f, ValueW = 90f;

            var go = new GameObject("Slider_" + it.name, typeof(RectTransform));
            go.transform.SetParent(body, false);
            var bg = go.AddComponent<Image>();
            bg.color = MenuKit.BtnBg;
            bg.raycastTarget = false;
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(w, RowH);
            // The same amber hairline along the top as every MenuKit button.
            var edge = Child(go.transform, "Edge", new Vector2(0f, 1f), new Vector2(1f, 1f));
            edge.pivot = new Vector2(0.5f, 1f);
            edge.offsetMin = new Vector2(0f, -2f); edge.offsetMax = Vector2.zero;
            Img(edge, MenuKit.Line, false);

            MenuKit.Label(go.transform, it.name, MenuKit.Small, new Vector2(0f, 0.5f), new Vector2(PadX, 0f),
                          TextAnchor.MiddleLeft, Color.white, NameW - PadX, RowH, bold: true).name = "Name";
            var val = MenuKit.Label(go.transform, it.show(it.get()), MenuKit.Small, new Vector2(1f, 0.5f),
                                    new Vector2(-PadX, 0f), TextAnchor.MiddleRight, MenuKit.Accent,
                                    ValueW, RowH, bold: true);
            val.name = "Value";

            // The track: a finger-tall transparent catcher, the bar, the fill
            // and the handle - the shape UGUI's own slider is built in.
            var track = Child(go.transform, "Track", Vector2.zero, Vector2.one);
            track.offsetMin = new Vector2(NameW, 0f);
            track.offsetMax = new Vector2(-(ValueW + PadX * 2f), 0f);
            Img(track, new Color(0f, 0f, 0f, 0f), true);
            var bar = Child(track, "Bar", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            bar.sizeDelta = new Vector2(0f, 8f);
            Img(bar, new Color(0f, 0f, 0f, 0.6f), false);
            var fillArea = Child(track, "FillArea", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(0f, 8f);
            var fill = Child(fillArea, "Fill", Vector2.zero, new Vector2(0f, 1f));
            fill.sizeDelta = Vector2.zero;
            Img(fill, MenuKit.Accent, false);
            // The Slider drives the handle's anchors on BOTH axes (0..1 across
            // the track), so its height is the area's: 28 of the row's 40.
            var handleArea = Child(track, "HandleArea", Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(8f, 6f); handleArea.offsetMax = new Vector2(-8f, -6f);
            var handle = Child(handleArea, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.sizeDelta = new Vector2(16f, 0f);
            Img(handle, new Color(0.92f, 0.92f, 0.92f, 1f), false);

            var s = go.AddComponent<Slider>();
            s.targetGraphic = bg;
            s.fillRect = fill;
            s.handleRect = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.wholeNumbers = true;
            s.minValue = it.min;
            s.maxValue = it.max;
            s.SetValueWithoutNotify(it.get());
            // MenuKit's button tints, so a selected slider reads exactly like a
            // selected button: gold, brighter than the mouse hover.
            var c = s.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.35f, 1.35f, 1.45f);
            c.pressedColor = new Color(1f, 0.80f, 0.30f);
            c.selectedColor = new Color(1.60f, 1.30f, 0.60f);
            c.disabledColor = new Color(0.7f, 0.7f, 0.7f);
            s.colors = c;
            var item = it;
            s.onValueChanged.AddListener(v =>
            {
                int n = Mathf.RoundToInt(v);
                item.set(n);
                val.text = item.show(n);
            });
            Tag(go, it.blurb);
            return s;
        }

        static RectTransform Child(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Img(RectTransform rt, Color c, bool raycast)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            i.raycastTarget = raycast;
        }

        /// <summary>Destroy in play mode or in the preview tool (edit mode).</summary>
        static void Kill(GameObject go)
        {
            if (Application.isPlaying) { go.SetActive(false); Destroy(go); } else DestroyImmediate(go);
        }
    }

    /// <summary>Tells the settings page's help line which row the cursor or
    /// the mouse is on, and remembers it for <see cref="SettingsPanel.Refocus"/>.</summary>
    public class SettingsHelpTag : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public SettingsPanel panel;
        public string text;

        public void OnPointerEnter(PointerEventData e) { if (panel != null) panel.ShowHelp(text); }

        public void OnSelect(BaseEventData e)
        {
            if (panel == null) return;
            panel.lastSel = gameObject;
            panel.ShowHelp(text);
        }
    }
}
